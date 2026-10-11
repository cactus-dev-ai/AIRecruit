from __future__ import annotations

from pathlib import Path

from app.resume_agent.documents import extract_text
from app.resume_agent.errors import ResumeImportError


def optimize_if_needed(path: Path, extension: str, target_bytes: int) -> tuple[Path, str, int, int, bool]:
    if extension.upper() != "JSON":
        raise ResumeImportError("UNSUPPORTED_FILE_TYPE", "系统仅支持JSON简历。")
    size = path.stat().st_size
    return path, extract_text(path, extension), size, size, False
