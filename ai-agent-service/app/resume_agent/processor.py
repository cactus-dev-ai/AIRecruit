from __future__ import annotations

import shutil
import uuid
from datetime import datetime, timezone
from pathlib import Path

from app.resume_agent.documents import safe_resolve, sha256_file, split_document, validate_source
from app.resume_agent.errors import ResumeImportError
from app.resume_agent.json_source import apply_json_identity, build_local_json_resume, load_json_resume
from app.resume_agent.models import ImportTask, PreparedResume
from app.resume_agent.optimizer import optimize_if_needed
from app.task_worker.repository import TaskRepository
from config.settings import get_settings


class ResumeImportProcessor:
    def __init__(self, repository: TaskRepository) -> None:
        self.repository = repository
        self.settings = get_settings()

    async def process(self, task: ImportTask) -> None:
        source = safe_resolve(self.settings.resume_temp_root, task.temp_file_path)
        split_relative = Path("SplitOutput") / f"task-{task.task_id}-{uuid.uuid4().hex}"
        split_directory = safe_resolve(self.settings.resume_temp_root, str(split_relative))
        detected = 0
        try:
            validate_source(
                source,
                task.file_extension,
                self.settings.resume_hard_source_file_bytes,
                self.settings.resume_json_max_file_bytes,
            )
            parts = split_document(source, task.file_extension, split_directory)
            detected = len(parts)
            self.repository.mark_split(task, len(parts))
            for part in parts:
                prepared_path, text, original_size, final_size, optimized = optimize_if_needed(
                    Path(part.path), task.file_extension, self.settings.resume_target_file_bytes
                )
                json_source = load_json_resume(Path(part.path), self.settings.resume_json_max_file_bytes)
                parsed = build_local_json_resume(json_source)
                talent_id, resume_source = apply_json_identity(parsed, json_source, task.original_file_name)
                model_name, prompt_version = "LOCAL_JSON", "json-local-v1"
                now = datetime.now(timezone.utc)
                formal_relative = Path("JSON") / f"{now.year:04d}" / f"{now.month:02d}" / f"{uuid.uuid4().hex}.json"
                formal = safe_resolve(self.settings.resume_root, str(formal_relative))
                formal.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(prepared_path, formal)
                prepared = PreparedResume(
                    path=str(prepared_path), sequence_no=part.sequence_no,
                    source_page_start=part.source_page_start, source_page_end=part.source_page_end,
                    text=text, original_size=original_size, final_size=final_size,
                    file_hash=sha256_file(formal), is_optimized=optimized,
                    talent_id=talent_id, resume_source=resume_source,
                )
                try:
                    self.repository.save_prepared_resume(
                        task, prepared, parsed, str(formal_relative), model_name, prompt_version
                    )
                except Exception:
                    formal.unlink(missing_ok=True)  # 数据库未提交，正式副本不能遗留。
                    raise
            self.repository.complete_resume_import(task, len(parts))
        except ResumeImportError as exc:
            failed_relative = _move_source_to_failed(source, task, self.settings.resume_temp_root)
            try: _safe_remove_tree(split_directory, self.settings.resume_temp_root)
            except Exception: pass
            self.repository.fail_resume_import(task, exc.code, exc.message, detected, failed_relative)
            return
        except Exception:
            failed_relative = _move_source_to_failed(source, task, self.settings.resume_temp_root)
            try: _safe_remove_tree(split_directory, self.settings.resume_temp_root)
            except Exception: pass
            self.repository.fail_resume_import(task, "RESUME_IMPORT_UNEXPECTED", "简历导入发生未预期错误。", detected, failed_relative)
            return
        try:
            source.unlink(missing_ok=True)
            _safe_remove_tree(split_directory, self.settings.resume_temp_root)
        except Exception:
            self.repository.record_cleanup_failure(task)


def _move_source_to_failed(source: Path, task: ImportTask, temp_root: Path) -> str | None:
    if not source.exists(): return None
    try:
        relative = Path("Failed") / task.batch_key.replace("-", "") / f"{uuid.uuid4().hex}{source.suffix.lower()}"
        target = safe_resolve(temp_root, str(relative))
        target.parent.mkdir(parents=True, exist_ok=True)
        source.replace(target)
        return str(relative)
    except OSError:
        return None


def _safe_remove_tree(path: Path, root: Path) -> None:
    if not path.exists(): return
    checked = safe_resolve(root, str(path.resolve().relative_to(root.resolve())))
    if checked == root.resolve(): raise ResumeImportError("INVALID_STORAGE_PATH", "禁止清理临时根目录。")
    shutil.rmtree(checked)
