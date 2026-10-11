from __future__ import annotations

from app.rag_agent.chunking import content_hash, split_knowledge, token_count
from app.rag_agent.embedding import HashingTextEmbedder


def test_embedding_is_deterministic_and_round_trips() -> None:
    embedder = HashingTextEmbedder(dimension=256)
    first = embedder.embed("电机电磁设计 Ansys Maxwell 绕组理论")
    second = embedder.embed("电机电磁设计 Ansys Maxwell 绕组理论")
    assert first == second
    restored = embedder.deserialize(embedder.serialize(first))
    assert len(restored) == 256
    assert embedder.cosine(first, restored) > 0.99999


def test_embedding_ranks_related_recruitment_text_higher() -> None:
    embedder = HashingTextEmbedder(dimension=1024)
    query = embedder.embed("电机电磁设计和Maxwell仿真经验")
    related = embedder.embed("负责驱动电机电磁计算，熟练使用Ansys Maxwell和绕组理论")
    unrelated = embedder.embed("负责行政考勤、薪酬核算和员工关系管理")
    assert embedder.cosine(query, related) > embedder.cosine(query, unrelated)


def test_chunking_respects_limit_and_keeps_overlap() -> None:
    content = "标题\n\n" + "电机设计与仿真。" * 100 + "\n\n" + "供应商评估与问题闭环。" * 80
    chunks = split_knowledge(content, max_chars=240, overlap_chars=30)
    assert len(chunks) > 2
    assert all(0 < len(chunk) <= 240 for chunk in chunks)
    assert chunks[0][-30:] in chunks[1]
    assert token_count(chunks[0]) > 0
    assert len(content_hash(chunks[0])) == 64
