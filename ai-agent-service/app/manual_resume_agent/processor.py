from __future__ import annotations

import asyncio
import logging
import shutil
import uuid
from datetime import datetime, timezone
from pathlib import Path

from app.deepseek_client import parse_resume
from app.manual_resume_agent.identity import detect_manual_resume_identity
from app.resume_agent.documents import extract_text, safe_resolve, sha256_file, validate_source
from app.resume_agent.errors import ResumeImportError
from app.resume_agent.models import ImportTask, PreparedResume
from app.task_worker.repository import TaskRepository
from config.settings import get_settings


logger = logging.getLogger(__name__)


class ManualResumeImportAgent:
    """Claims only MANUAL_RESUME_IMPORT tasks and parses each upload as one resume."""

    def __init__(self, repository: TaskRepository) -> None:
        self.repository = repository
        self.settings = get_settings()

    async def run_once(self) -> bool:
        task = await asyncio.to_thread(self.repository.claim_manual_resume_import)
        if task is None:
            return False
        logger.info("Claimed manual resume task task_id=%s import_file_id=%s", task.task_id, task.import_file_id)
        await self.process(task)
        return True

    async def process(self, task: ImportTask) -> None:
        source = safe_resolve(self.settings.resume_temp_root, task.temp_file_path)
        formal: Path | None = None
        try:
            validate_source(source, task.file_extension, self.settings.resume_hard_source_file_bytes)
            text = extract_text(source, task.file_extension)
            parsed, model_name, prompt_version = await parse_resume(text)
            now = datetime.now(timezone.utc)
            suffix = source.suffix.lower()
            formal_relative = Path("MANUAL") / f"{now.year:04d}" / f"{now.month:02d}" / f"{uuid.uuid4().hex}{suffix}"
            formal = safe_resolve(self.settings.resume_root, str(formal_relative))
            formal.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, formal)
            size = formal.stat().st_size
            talent_id, resume_source = detect_manual_resume_identity(task.original_file_name, text)
            prepared = PreparedResume(
                path=str(source), sequence_no=1, text=text, original_size=size, final_size=size,
                file_hash=sha256_file(formal), is_optimized=False,
                talent_id=talent_id, resume_source=resume_source,
            )
            try:
                await asyncio.to_thread(
                    self.repository.save_prepared_resume, task, prepared, parsed,
                    str(formal_relative), model_name, prompt_version, False,
                )
            except Exception:
                formal.unlink(missing_ok=True)
                raise
            await asyncio.to_thread(self.repository.complete_resume_import, task, 1)
            source.unlink(missing_ok=True)
        except ResumeImportError as exc:
            failed_relative = _move_to_failed(source, task, self.settings.resume_temp_root)
            await asyncio.to_thread(self.repository.fail_resume_import, task, exc.code, exc.message, 0, failed_relative)
        except Exception as exc:
            if formal is not None:
                formal.unlink(missing_ok=True)
            logger.exception("Manual resume import failed task_id=%s", task.task_id)
            failed_relative = _move_to_failed(source, task, self.settings.resume_temp_root)
            message = f"人工上传简历解析失败：{type(exc).__name__}: {str(exc)[:1200]}"
            await asyncio.to_thread(self.repository.fail_resume_import, task, "MANUAL_RESUME_IMPORT_FAILED", message, 0, failed_relative)


def _move_to_failed(source: Path, task: ImportTask, temp_root: Path) -> str | None:
    if not source.exists():
        return None
    try:
        relative = Path("Failed") / task.batch_key.replace("-", "") / f"{uuid.uuid4().hex}{source.suffix.lower()}"
        target = safe_resolve(temp_root, str(relative))
        target.parent.mkdir(parents=True, exist_ok=True)
        source.replace(target)
        return str(relative)
    except OSError:
        return None
