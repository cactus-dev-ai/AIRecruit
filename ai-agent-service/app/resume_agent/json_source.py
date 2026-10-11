from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

from app.resume_agent.errors import ResumeImportError
from app.resume_agent.models import ParsedResume


MAX_NAME_CHARS = 100
MAX_TALENT_ID_CHARS = 100
MAX_SOURCE_ROUTE_CHARS = 200
MAX_HEADLINE_CHARS = 200
MAX_VISIBLE_TEXT_CHARS = 500_000
SOURCE_PATTERN = re.compile(r"^[A-Za-z0-9-]{1,100}$")
WHITESPACE_PATTERN = re.compile(r"[ \t\u00a0]+")
TALENT_HEADER_PATTERN = re.compile(r"^人才ID\s*[:：].*活跃时间\s*[:：]")
UNLOCK_PATTERN = re.compile(r"^解锁\d+项人才行为分析")
NOTIFICATION_BADGE_PATTERN = re.compile(r"^\d{1,3}$")
MASKED_PERSON_NAME_PATTERN = re.compile(r"^[\u3400-\u9fff·]{1,8}(?:先生|女士)$")
EXPERIENCE_PATTERN = re.compile(r"(?<!\d)(\d{1,2})年经验")
EDUCATION_PATTERN = re.compile(r"博士(?:研究生)?|硕士(?:研究生)?|本科|大专|中专|高中|初中")
UI_LINES = {
    "通知", "手机版", "帮助", "企业管理", "扫码分享", "在线简历", "Resume", "举报",
    "电话沟通", "附件个人信息", "人才罗盘", "立即Hi聊", "不感兴趣",
}
CONTENT_SECTION_LINES = {"求职意向", "个人优势", "工作经历", "项目经验", "教育经历"}


@dataclass(frozen=True)
class JsonResumeSource:
    talent_id: str
    source_route: str
    candidate_name: str
    headline: str | None
    visible_text: str


def load_json_resume(path: Path, max_file_bytes: int) -> JsonResumeSource:
    if not path.is_file() or path.stat().st_size <= 0:
        raise ResumeImportError("EMPTY_FILE", "JSON简历为空或不存在。")
    if path.stat().st_size > max_file_bytes:
        raise ResumeImportError("JSON_FILE_TOO_LARGE", "JSON简历超过10MB安全上限。")
    try:
        with path.open("r", encoding="utf-8-sig") as stream:
            value = json.load(stream, object_pairs_hook=_reject_duplicate_keys)
    except ResumeImportError:
        raise
    except (UnicodeDecodeError, json.JSONDecodeError, OSError, RecursionError) as exc:
        raise ResumeImportError("JSON_STRUCTURE_INVALID", "JSON简历编码或结构无效。") from exc
    if not isinstance(value, dict):
        raise ResumeImportError("JSON_STRUCTURE_INVALID", "JSON简历根节点必须是对象。")
    talent_id = _required_text(value, "talent_id", MAX_TALENT_ID_CHARS)
    source_route = _required_text(value, "source_route", MAX_SOURCE_ROUTE_CHARS)
    name = _required_text(value, "candidate_name", MAX_NAME_CHARS)
    visible_text = _required_text(value, "visible_text", MAX_VISIBLE_TEXT_CHARS)
    name, cleaned_visible_text = clean_candidate_identity(name, clean_visible_text(visible_text))
    headline_value = value.get("headline")
    if headline_value is not None and not isinstance(headline_value, str):
        raise ResumeImportError("JSON_FIELD_INVALID", "JSON字段headline必须是字符串。")
    headline = headline_value.strip() if isinstance(headline_value, str) else None
    if headline and len(headline) > MAX_HEADLINE_CHARS:
        raise ResumeImportError("JSON_FIELD_INVALID", "JSON字段headline超过200字。")
    return JsonResumeSource(
        talent_id=talent_id,
        source_route=source_route,
        candidate_name=name,
        headline=headline or None,
        visible_text=cleaned_visible_text,
    )


def resume_source_from_file_name(file_name: str, talent_id: str) -> str:
    stem = Path(file_name).stem
    source, separator, file_talent_id = stem.partition("_")
    if not separator or not SOURCE_PATTERN.fullmatch(source):
        raise ResumeImportError("JSON_FILE_NAME_INVALID", "JSON文件名必须为“来源_人才ID.json”。")
    normalized_file_id = file_talent_id.casefold()
    normalized_talent_id = talent_id.casefold()
    # New files append the external job id: SOURCE_TALENT_JOB.json.
    # Continue accepting the legacy SOURCE_TALENT.json form.
    if normalized_file_id != normalized_talent_id and not normalized_file_id.startswith(normalized_talent_id + "_"):
        raise ResumeImportError("JSON_TALENT_ID_MISMATCH", "JSON文件名人才ID与文件内容不一致。")
    return source.upper()


def apply_json_identity(parsed: ParsedResume, source: JsonResumeSource, file_name: str) -> tuple[str, str]:
    resume_source = resume_source_from_file_name(file_name, source.talent_id)
    parsed.name = source.candidate_name
    parsed.phone = None
    parsed.email = None
    if source.headline:
        parsed.current_title = source.headline
    return source.talent_id, resume_source


def build_local_json_resume(source: JsonResumeSource) -> ParsedResume:
    """Build the deterministic profile stored for JSON imports without invoking an AI model."""
    experience = EXPERIENCE_PATTERN.search(source.visible_text)
    education = EDUCATION_PATTERN.search(source.visible_text)
    intended_title = _first_job_intention(source.visible_text)
    return ParsedResume(
        name=source.candidate_name,
        phone=None,
        email=None,
        highest_education=education.group(0) if education else None,
        total_experience_months=int(experience.group(1)) * 12 if experience else None,
        current_title=intended_title,
    )


def build_local_json_profile(candidate_name: str, visible_text: str) -> ParsedResume:
    """Rebuild a deterministic profile from JSON text already stored in the database."""
    name, cleaned_text = clean_candidate_identity(candidate_name, visible_text)
    return build_local_json_resume(JsonResumeSource("", "", name, None, cleaned_text))


def clean_visible_text(text: str) -> str:
    lines = [WHITESPACE_PATTERN.sub(" ", line).strip() for line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n")]
    lines = [line for line in lines if line]
    footer = next((index for index, line in enumerate(lines) if "声明：以上人才信息仅供" in line or line == "操作动态"), len(lines))
    before_resume_content = True
    result: list[str] = []
    for line in lines[:footer]:
        if line in CONTENT_SECTION_LINES:
            before_resume_content = False
        if (line in UI_LINES or TALENT_HEADER_PATTERN.search(line) or UNLOCK_PATTERN.search(line) or
                line.startswith("该人才偏好电话，推荐您") or line.startswith("您可对候选人的在职状态进行相关询问") or
                line.startswith("候选人似乎处于离职状态") or
                (before_resume_content and NOTIFICATION_BADGE_PATTERN.fullmatch(line)) or _is_repeated_watermark(line)):
            continue
        result.append(line)
    normalized = "\n".join(result).strip()
    if not normalized:
        raise ResumeImportError("NO_TEXT_CONTENT", "JSON简历清洗后没有可读取正文。")
    return normalized


def clean_candidate_identity(candidate_name: str, cleaned_text: str) -> tuple[str, str]:
    lines = [line.strip() for line in cleaned_text.splitlines() if line.strip()]
    if lines and MASKED_PERSON_NAME_PATTERN.fullmatch(lines[0]):
        return lines[0], "\n".join(lines)
    if len(lines) >= 2 and not MASKED_PERSON_NAME_PATTERN.fullmatch(lines[0]) and MASKED_PERSON_NAME_PATTERN.fullmatch(lines[1]):
        return lines[1], "\n".join(lines[1:])
    return candidate_name.strip(), cleaned_text


def _first_job_intention(text: str) -> str | None:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    for index, line in enumerate(lines[:-1]):
        if line == "求职意向":
            return lines[index + 1][:200] or None
    return None


def _is_repeated_watermark(line: str) -> bool:
    parts = line.replace("\u00a0", " ").split()
    if len(parts) >= 3 and len(parts[0]) >= 6 and len(set(parts)) == 1:
        return True
    compact = "".join(parts)
    for length in range(6, len(compact) // 3 + 1):
        if len(compact) % length == 0 and len(compact) // length >= 3 and compact == compact[:length] * (len(compact) // length):
            return True
    return False


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ResumeImportError("JSON_DUPLICATE_KEY", f"JSON包含重复字段：{key[:100]}。")
        result[key] = value
    return result


def _required_text(value: dict[str, object], key: str, max_chars: int) -> str:
    item = value.get(key)
    if not isinstance(item, str) or not item.strip():
        raise ResumeImportError("JSON_FIELD_REQUIRED", f"JSON缺少有效字段：{key}。")
    result = item.strip()
    if len(result) > max_chars:
        raise ResumeImportError("JSON_FIELD_INVALID", f"JSON字段{key}内容过长。")
    return result


def _normalize_text(text: str) -> str:
    text = text.replace("\r\n", "\n").replace("\r", "\n").replace("\u0000", "")
    lines = [" ".join(line.split()) for line in text.split("\n")]
    normalized = "\n".join(line for line in lines if line).strip()
    if not normalized:
        raise ResumeImportError("NO_TEXT_CONTENT", "JSON简历没有可读取文本。")
    return normalized
