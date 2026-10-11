import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException

from app.database import check_database
from app.deepseek_client import generate_interview_question_set, generate_interview_report, generate_knowledge_draft, test_completion
from pydantic import AliasChoices, BaseModel, Field
from app.rag_agent.embedding import HashingTextEmbedder
from app.rag_agent.models import RagSearchRequest, RagSearchResponse
from app.rag_agent.processor import RagSearchService
from app.rag_agent.repository import RagRepository
from app.task_worker.worker import TaskWorker
from app.resume_agent.errors import ResumeImportError
from config.settings import get_settings


settings = get_settings()
logger = logging.getLogger(__name__)
worker: TaskWorker | None = None
worker_task: asyncio.Task | None = None
worker_lock = asyncio.Lock()
rag_search = RagSearchService(
    RagRepository(),
    HashingTextEmbedder(settings.rag_embedding_dimension, settings.rag_embedding_model),
)


@asynccontextmanager
async def lifespan(_: FastAPI):
    if settings.worker_enabled:
        await start_worker()
    try:
        yield
    finally:
        await pause_worker()


app = FastAPI(title="RecruitAI Agent Service", version="1.2.0", lifespan=lifespan)


class KnowledgeDraftRequest(BaseModel):
    title: str = Field(min_length=1, max_length=300)
    document_type: str = Field(validation_alias=AliasChoices("document_type", "documentType"), min_length=1, max_length=30)
    content: str | None = Field(default=None, max_length=50000)
    instruction: str | None = Field(default=None, max_length=1000)


class InterviewQuestionSetRequest(BaseModel):
    context: dict


class InterviewReportRequest(BaseModel):
    context: dict


async def start_worker() -> bool:
    global worker, worker_task
    async with worker_lock:
        if worker_task is not None and not worker_task.done():
            return False
        worker = TaskWorker()
        worker_task = asyncio.create_task(worker.run_forever())
        return True


async def pause_worker() -> bool:
    global worker, worker_task
    async with worker_lock:
        if worker_task is None or worker_task.done():
            return False
        worker.stop()
        return True


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "environment": settings.app_env}


@app.get("/worker/status")
def worker_status() -> dict[str, bool]:
    return {"running": worker_task is not None and not worker_task.done()}


@app.post("/worker/start")
async def worker_start() -> dict[str, bool]:
    await start_worker()
    return {"running": True}


@app.post("/worker/pause")
async def worker_pause() -> dict[str, bool]:
    await pause_worker()
    return {"running": False}


@app.get("/health/database")
def database_health() -> dict[str, str | int]:
    try:
        return {"status": "ok", **check_database()}
    except Exception as exc:
        raise HTTPException(status_code=503, detail="database unavailable") from exc


@app.get("/health/deepseek")
async def deepseek_health() -> dict[str, str]:
    try:
        return {"status": "ok", **await test_completion()}
    except Exception as exc:
        raise HTTPException(status_code=503, detail="deepseek unavailable") from exc


@app.post("/rag/search", response_model=RagSearchResponse)
def search_knowledge(request: RagSearchRequest) -> RagSearchResponse:
    try:
        return rag_search.search(request.query, request.top_k, request.document_type)
    except Exception as exc:
        raise HTTPException(status_code=503, detail="rag search unavailable") from exc


@app.post("/knowledge/draft")
async def knowledge_draft(request: KnowledgeDraftRequest) -> dict[str, str]:
    try:
        content = await generate_knowledge_draft(request.title, request.document_type, request.content, request.instruction)
        return {"content": content}
    except ResumeImportError as exc:
        logger.warning("Knowledge draft failed code=%s message=%s", exc.code, exc.message)
        raise HTTPException(status_code=503, detail=exc.message) from exc
    except Exception as exc:
        logger.exception("Knowledge draft failed unexpectedly")
        raise HTTPException(status_code=503, detail="knowledge draft unavailable") from exc


@app.post("/interview/question-set/draft")
async def interview_question_set_draft(request: InterviewQuestionSetRequest) -> dict:
    try:
        questions, model, prompt_version = await generate_interview_question_set(request.context)
        return {"questions": questions, "model": model, "prompt_version": prompt_version}
    except Exception as exc:
        raise HTTPException(status_code=503, detail="interview question set unavailable") from exc


@app.post("/interview/report/generate")
async def interview_report_generate(request: InterviewReportRequest) -> dict:
    try:
        report, model, prompt_version = await generate_interview_report(request.context)
        return {**report, "model": model, "prompt_version": prompt_version}
    except Exception as exc:
        raise HTTPException(status_code=503, detail="interview report unavailable") from exc


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host=settings.app_host, port=settings.app_port, reload=False)
