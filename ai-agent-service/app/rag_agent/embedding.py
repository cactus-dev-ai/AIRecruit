from __future__ import annotations

import hashlib
import math
import re
import struct
import unicodedata


WORD = re.compile(r"[a-z0-9][a-z0-9_+.#/-]*", re.IGNORECASE)
CJK = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff]+")


class HashingTextEmbedder:
    """Deterministic, offline text embedding for Chinese recruitment knowledge.

    It combines Latin word features with Chinese character uni/bi/trigrams and
    serializes an L2-normalized float32 vector in explicit little-endian order.
    """

    def __init__(self, dimension: int = 1024, model_name: str = "hashing-char-ngram-v1") -> None:
        if dimension < 128:
            raise ValueError("embedding dimension must be at least 128")
        self.dimension = dimension
        self.model_name = model_name

    def embed(self, text: str) -> list[float]:
        normalized = unicodedata.normalize("NFKC", text).lower()
        vector = [0.0] * self.dimension
        for feature, weight in self._features(normalized):
            digest = hashlib.blake2b(feature.encode("utf-8"), digest_size=8, person=b"RecruitAI").digest()
            value = int.from_bytes(digest, "little", signed=False)
            index = value % self.dimension
            sign = -1.0 if value & (1 << 63) else 1.0
            vector[index] += sign * weight
        norm = math.sqrt(sum(value * value for value in vector))
        if norm:
            vector = [value / norm for value in vector]
        return vector

    def serialize(self, vector: list[float]) -> bytes:
        if len(vector) != self.dimension:
            raise ValueError("vector dimension mismatch")
        return struct.pack(f"<{self.dimension}f", *vector)

    def deserialize(self, value: bytes) -> tuple[float, ...]:
        expected = self.dimension * 4
        if len(value) != expected:
            raise ValueError(f"invalid vector byte length: expected {expected}, got {len(value)}")
        return struct.unpack(f"<{self.dimension}f", value)

    @staticmethod
    def cosine(left: list[float] | tuple[float, ...], right: list[float] | tuple[float, ...]) -> float:
        if len(left) != len(right):
            raise ValueError("vector dimension mismatch")
        return float(sum(a * b for a, b in zip(left, right, strict=True)))

    @staticmethod
    def _features(text: str):
        for word in WORD.findall(text):
            yield f"w:{word}", 1.4
        for sequence in CJK.findall(text):
            for char in sequence:
                yield f"c1:{char}", 0.35
            for size, weight in ((2, 1.0), (3, 1.2)):
                for index in range(max(0, len(sequence) - size + 1)):
                    yield f"c{size}:{sequence[index:index + size]}", weight
