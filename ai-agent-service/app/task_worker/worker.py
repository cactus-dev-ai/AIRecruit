from __future__ import annotations

import asyncio
import logging

from app.resume_agent.processor import ResumeImportProcessor
from app.manual_resume_agent.processor import ManualResumeImportAgent
from app.deepseek_client import parse_resume, score_candidate_job
from app.rag_agent.embedding import HashingTextEmbedder
from app.rag_agent.processor import KnowledgeEmbedProcessor
from app.rag_agent.repository import RagRepository
from app.task_worker.repository import TaskRepository
from config.settings import get_settings


logger = logging.getLogger(__name__)


class TaskWorker:
    def __init__(self) -> None:
        self.settings = get_settings()
        self.repository = TaskRepository()
        self.resume_processor = ResumeImportProcessor(self.repository)
        self.manual_resume_agent = ManualResumeImportAgent(self.repository)
        self.rag_repository = RagRepository()
        self.embedder = HashingTextEmbedder(self.settings.rag_embedding_dimension, self.settings.rag_embedding_model)
        self.knowledge_processor = KnowledgeEmbedProcessor(self.rag_repository, self.embedder)
        self._stopping = asyncio.Event()

    async def run_maintenance(self) -> None:
        await asyncio.to_thread(self.repository.expire_abandoned_resume_tasks)
        await asyncio.to_thread(self.repository.expire_abandoned_resume_analysis_tasks)
        recovered_match_tasks = await asyncio.to_thread(self.repository.expire_abandoned_match_score_tasks)
        if recovered_match_tasks:
            logger.warning("Recovered abandoned match score tasks count=%s", recovered_match_tasks)
        await asyncio.to_thread(self.rag_repository.expire_abandoned_knowledge_tasks)
        failed_analysis = await asyncio.to_thread(self.repository.find_failed_resume_analysis)
        if failed_analysis is not None:
            await asyncio.to_thread(self.repository.purge_failed_resume_analysis, failed_analysis)

    async def run_once(self) -> bool:
        task = await asyncio.to_thread(self.repository.claim_resume_import)
        if task is not None:
            logger.info("Claimed resume import task task_id=%s import_file_id=%s", task.task_id, task.import_file_id)
            await self.resume_processor.process(task)
            return True
        analysis_task = await asyncio.to_thread(self.repository.claim_resume_analysis)
        if analysis_task is not None:
            logger.info("Claimed resume analysis task task_id=%s resume_id=%s", analysis_task.task_id, analysis_task.resume_id)
            try:
                parsed, model_name, prompt_version = await parse_resume(analysis_task.raw_text)
                await asyncio.to_thread(self.repository.complete_resume_analysis, analysis_task, parsed, model_name, prompt_version)
            except Exception as exc:
                logger.exception("Resume analysis failed task_id=%s", analysis_task.task_id)
                deleted_files = await asyncio.to_thread(self.repository.purge_failed_resume_analysis, analysis_task)
                logger.warning(
                    "Purged unusable resume after analysis failure task_id=%s candidate_id=%s deleted_files=%s reason=%s",
                    analysis_task.task_id, analysis_task.candidate_id, deleted_files, str(exc)[:500],
                )
            return True
        match_task=await asyncio.to_thread(self.repository.claim_match_score)
        if match_task is not None:
            logger.info("Claimed match score task task_id=%s candidate_job_id=%s", match_task.task_id, match_task.candidate_job_id)
            try:
                context=await asyncio.to_thread(self.repository.load_match_context,match_task)
                result,model,prompt_version=await score_candidate_job(context)
                await asyncio.to_thread(self.repository.complete_match_score,match_task,context,result,model,prompt_version)
            except Exception as exc:
                logger.exception("Match score failed task_id=%s",match_task.task_id)
                await asyncio.to_thread(self.repository.fail_match_score,match_task,f"岗位评分失败：{type(exc).__name__}: {str(exc)[:1500]}")
            return True
        knowledge_task = await asyncio.to_thread(self.rag_repository.claim_knowledge_embed)
        if knowledge_task is None:
            return False
        logger.info("Claimed knowledge task task_id=%s document_id=%s", knowledge_task.task_id, knowledge_task.document_id)
        await asyncio.to_thread(self.knowledge_processor.process, knowledge_task)
        return True

    async def run_forever(self) -> None:
        while not self._stopping.is_set():
            try:
                await self.run_maintenance()
                runs = [self.run_once() for _ in range(max(1, self.settings.worker_concurrency))]
                manual_agent = getattr(self, "manual_resume_agent", None)
                if manual_agent is not None:
                    runs.insert(0, manual_agent.run_once())
                results = await asyncio.gather(*runs, return_exceptions=True)
                processed = False
                for result in results:
                    if isinstance(result, Exception):
                        logger.error("Task worker execution failed: %r", result)
                    else:
                        processed = processed or result
            except Exception:
                logger.exception("Task worker loop failed without exposing task content")
                processed = False
            if not processed:
                try: await asyncio.wait_for(self._stopping.wait(), timeout=self.settings.worker_poll_seconds)
                except TimeoutError: pass

    def stop(self) -> None:
        self._stopping.set()
