from __future__ import annotations

from dataclasses import dataclass

from pydantic import BaseModel, Field


@dataclass(frozen=True)
class KnowledgeTask:
    task_id: int
    document_id: int
    document_version: int
    business_key: str
    title: str
    document_type: str
    content: str


@dataclass(frozen=True)
class KnowledgeChunkData:
    index: int
    content: str
    token_count: int
    metadata_json: str
    content_hash: str
    vector: bytes


class RagSearchRequest(BaseModel):
    query: str = Field(min_length=1, max_length=2000)
    top_k: int = Field(default=5, ge=1, le=20)
    document_type: str | None = Field(default=None, max_length=30)


class RagSearchItem(BaseModel):
    document_id: int
    document_code: str
    title: str
    document_type: str
    chunk_index: int
    content: str
    score: float


class RagSearchResponse(BaseModel):
    model: str
    dimension: int
    items: list[RagSearchItem]
