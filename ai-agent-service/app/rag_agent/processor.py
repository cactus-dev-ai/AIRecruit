from __future__ import annotations

import logging

from app.rag_agent.chunking import content_hash, metadata, split_knowledge, token_count
from app.rag_agent.embedding import HashingTextEmbedder
from app.rag_agent.models import KnowledgeChunkData, KnowledgeTask, RagSearchResponse
from app.rag_agent.repository import RagRepository
from config.settings import get_settings


logger = logging.getLogger(__name__)


class KnowledgeEmbedProcessor:
    def __init__(self, repository: RagRepository, embedder: HashingTextEmbedder) -> None:
        self.repository = repository
        self.embedder = embedder
        self.settings = get_settings()

    def process(self, task: KnowledgeTask) -> None:
        try:
            texts = split_knowledge(task.content, self.settings.rag_chunk_chars, self.settings.rag_chunk_overlap_chars)
            chunks = [KnowledgeChunkData(
                index=index,
                content=text,
                token_count=token_count(text),
                metadata_json=metadata(task.document_id, task.document_version, index, len(texts), task.title, task.document_type),
                content_hash=content_hash(text),
                vector=self.embedder.serialize(self.embedder.embed(text)),
            ) for index, text in enumerate(texts)]
            self.repository.complete_knowledge_embed(task, chunks, self.embedder.model_name, self.embedder.dimension)
        except Exception as exc:
            logger.exception("Knowledge embedding failed task_id=%s document_id=%s", task.task_id, task.document_id)
            self.repository.fail_knowledge_embed(task, f"知识向量化失败：{type(exc).__name__}")


class RagSearchService:
    def __init__(self, repository: RagRepository, embedder: HashingTextEmbedder) -> None:
        self.repository = repository
        self.embedder = embedder

    def search(self, query: str, top_k: int = 5, document_type: str | None = None) -> RagSearchResponse:
        items = self.repository.search(query.strip(), top_k, document_type, self.embedder)
        return RagSearchResponse(model=self.embedder.model_name, dimension=self.embedder.dimension, items=items)
