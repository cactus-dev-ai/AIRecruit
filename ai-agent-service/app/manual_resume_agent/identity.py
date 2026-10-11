from __future__ import annotations

import re


_FILE_NAME_ID = re.compile(r"[（(](\d{5,100})[）)](?=\.[^.]+$|$)")
_TEXT_ID = re.compile(r"(?:51job\s*)?(?:人才ID|人才编号|候选人ID|简历ID|resume\s*id)\s*[:：#]?\s*(\d{5,100})", re.IGNORECASE)


def detect_manual_resume_identity(file_name: str, text: str) -> tuple[str | None, str]:
    """Return a trusted 51job talent id and source for a manually uploaded resume."""
    source_hint = file_name.lstrip().casefold().startswith("51job") or "前程无忧" in text[:4000] or "51job" in text[:4000].casefold()
    if not source_hint:
        return None, "HR_UPLOAD"

    file_match = _FILE_NAME_ID.search(file_name.strip())
    if file_match:
        return file_match.group(1), "51JOB"

    text_match = _TEXT_ID.search(text[:12000])
    return (text_match.group(1), "51JOB") if text_match else (None, "HR_UPLOAD")
