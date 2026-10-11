from __future__ import annotations

import hashlib
import json
import re


PARAGRAPH_BREAK = re.compile(r"\n\s*\n+")
TOKEN = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff]|[A-Za-z0-9][A-Za-z0-9_+.#/-]*")


def split_knowledge(content: str, max_chars: int = 1200, overlap_chars: int = 160) -> list[str]:
    if max_chars < 200:
        raise ValueError("max_chars must be at least 200")
    if overlap_chars < 0 or overlap_chars >= max_chars:
        raise ValueError("overlap_chars must be between 0 and max_chars")
    normalized = content.replace("\r\n", "\n").replace("\r", "\n").strip()
    if not normalized:
        raise ValueError("knowledge content is empty")
    paragraphs = [part.strip() for part in PARAGRAPH_BREAK.split(normalized) if part.strip()]
    chunks: list[str] = []
    current = ""
    for paragraph in paragraphs:
        remaining = paragraph
        while remaining:
            available = max_chars - len(current) - (2 if current else 0)
            if available <= 0:
                chunks.append(current)
                current = current[-overlap_chars:] if overlap_chars else ""
                continue
            if len(remaining) <= available:
                current = f"{current}\n\n{remaining}" if current else remaining
                remaining = ""
                continue
            split_at = _safe_split(remaining, available)
            piece, remaining = remaining[:split_at].strip(), remaining[split_at:].strip()
            current = f"{current}\n\n{piece}" if current else piece
            chunks.append(current)
            current = current[-overlap_chars:] if overlap_chars else ""
    if current and (not chunks or current != chunks[-1]):
        chunks.append(current)
    return chunks


def token_count(text: str) -> int:
    return len(TOKEN.findall(text))


def content_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest().upper()


def metadata(document_id: int, version: int, chunk_index: int, chunk_count: int, title: str, document_type: str) -> str:
    return json.dumps({
        "documentId": document_id,
        "documentVersion": version,
        "chunkIndex": chunk_index,
        "chunkCount": chunk_count,
        "title": title,
        "documentType": document_type,
    }, ensure_ascii=False, separators=(",", ":"))


def _safe_split(text: str, limit: int) -> int:
    if limit <= 0:
        return 1
    floor = max(1, int(limit * 0.55))
    for marker in ("\n", "。", "；", ";", "，", ",", " "):
        position = text.rfind(marker, floor, limit + 1)
        if position >= floor:
            return min(position + len(marker), limit)
    return min(limit, len(text))
