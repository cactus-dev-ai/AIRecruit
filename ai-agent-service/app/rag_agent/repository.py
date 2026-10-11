from __future__ import annotations

import json

import pyodbc

from app.database import build_connection_string
from app.rag_agent.embedding import HashingTextEmbedder
from app.rag_agent.models import KnowledgeChunkData, KnowledgeTask, RagSearchItem
from config.settings import get_settings


class RagRepository:
    def claim_knowledge_embed(self) -> KnowledgeTask | None:
        settings = get_settings()
        sql = """
        SET NOCOUNT ON;
        DECLARE @Claimed TABLE(TaskId BIGINT,DocumentId BIGINT,BusinessKey VARCHAR(200));
        ;WITH NextTask AS (
          SELECT TOP (1) t.* FROM dbo.AITask t WITH (UPDLOCK,READPAST,ROWLOCK)
          JOIN dbo.KnowledgeDocument d ON d.Id=t.KnowledgeDocumentId
          WHERE t.TaskType='KNOWLEDGE_EMBED' AND t.Status='WAITING'
            AND t.NextRunTime<=SYSUTCDATETIME() AND d.IsDeleted=0
          ORDER BY t.Priority DESC,t.CreateTime ASC
        )
        UPDATE NextTask SET Status='RUNNING',WorkerId=?,LockedUntil=DATEADD(MINUTE,?,SYSUTCDATETIME()),
          StartTime=COALESCE(StartTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME()
        OUTPUT inserted.Id,inserted.KnowledgeDocumentId,inserted.BusinessKey INTO @Claimed;
        SELECT c.TaskId,d.Id,d.Version,c.BusinessKey,d.Title,d.DocumentType,d.Content
        FROM @Claimed c JOIN dbo.KnowledgeDocument d ON d.Id=c.DocumentId;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql, settings.worker_id, settings.worker_lease_minutes).fetchone()
            if row is None:
                return None
            return KnowledgeTask(
                task_id=int(row[0]), document_id=int(row[1]), document_version=int(row[2]),
                business_key=str(row[3]), title=str(row[4]), document_type=str(row[5]), content=str(row[6]),
            )

    def expire_abandoned_knowledge_tasks(self) -> int:
        sql = """
        SET NOCOUNT ON;
        DECLARE @Expired TABLE(DocumentId BIGINT,WillRetry BIT);
        UPDATE dbo.AITask SET
          Status=CASE WHEN RetryCount<MaxRetryCount THEN 'WAITING' ELSE 'FAILED' END,
          RetryCount=CASE WHEN RetryCount<MaxRetryCount THEN RetryCount+1 ELSE RetryCount END,
          NextRunTime=CASE WHEN RetryCount<MaxRetryCount THEN DATEADD(MINUTE,5,SYSUTCDATETIME()) ELSE NextRunTime END,
          ErrorMessage=N'知识向量任务租约过期。',FinishTime=CASE WHEN RetryCount<MaxRetryCount THEN NULL ELSE SYSUTCDATETIME() END,
          WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME()
        OUTPUT inserted.KnowledgeDocumentId,CASE WHEN inserted.Status='WAITING' THEN 1 ELSE 0 END INTO @Expired
        WHERE TaskType='KNOWLEDGE_EMBED' AND Status='RUNNING' AND LockedUntil<SYSUTCDATETIME();
        UPDATE d SET Status='FAILED',UpdateTime=SYSUTCDATETIME()
        FROM dbo.KnowledgeDocument d JOIN @Expired e ON e.DocumentId=d.Id WHERE e.WillRetry=0;
        SELECT COUNT(*) FROM @Expired;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql).fetchone()
            return int(row[0]) if row else 0

    def complete_knowledge_embed(self, task: KnowledgeTask, chunks: list[KnowledgeChunkData], model: str, dimension: int) -> bool:
        connection = pyodbc.connect(build_connection_string(), timeout=30, autocommit=False)
        try:
            cursor = connection.cursor()
            row = cursor.execute(
                "SELECT Version,IsDeleted FROM dbo.KnowledgeDocument WITH (UPDLOCK,HOLDLOCK) WHERE Id=?",
                task.document_id,
            ).fetchone()
            if row is None or bool(row[1]) or int(row[0]) != task.document_version:
                cursor.execute(
                    "UPDATE dbo.AITask SET Status='CANCELLED',ErrorMessage=N'知识文档版本已变化，旧向量任务取消。',"
                    "FinishTime=SYSUTCDATETIME(),LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='RUNNING'",
                    task.task_id,
                )
                connection.commit()
                return False
            cursor.execute(
                "DELETE e FROM dbo.KnowledgeEmbedding e JOIN dbo.KnowledgeChunk c ON c.Id=e.KnowledgeChunkId WHERE c.KnowledgeDocumentId=?",
                task.document_id,
            )
            cursor.execute("DELETE dbo.KnowledgeChunk WHERE KnowledgeDocumentId=?", task.document_id)
            for chunk in chunks:
                cursor.execute(
                    "INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash) "
                    "OUTPUT inserted.Id VALUES(?,?,?,?,?,?)",
                    task.document_id, chunk.index, chunk.content, chunk.token_count, chunk.metadata_json, chunk.content_hash,
                )
                chunk_id = int(cursor.fetchone()[0])
                cursor.execute(
                    "INSERT dbo.KnowledgeEmbedding(KnowledgeChunkId,EmbeddingModel,Dimension,VectorData) VALUES(?,?,?,?)",
                    chunk_id, model, dimension, pyodbc.Binary(chunk.vector),
                )
            result = json.dumps({"model": model, "dimension": dimension, "chunkCount": len(chunks), "documentVersion": task.document_version}, ensure_ascii=False)
            cursor.execute(
                "UPDATE dbo.KnowledgeDocument SET Status='ACTIVE',UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Version=?;"
                "UPDATE dbo.AITask SET Status='SUCCESS',ResultJson=?,ErrorMessage=NULL,FinishTime=SYSUTCDATETIME(),"
                "LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='RUNNING';",
                task.document_id, task.document_version, result, task.task_id,
            )
            connection.commit()
            return True
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def fail_knowledge_embed(self, task: KnowledgeTask, message: str) -> None:
        safe_message = message[:2000]
        sql = """
        SET NOCOUNT ON;
        DECLARE @Retry BIT;
        SELECT @Retry=CASE WHEN RetryCount<MaxRetryCount THEN 1 ELSE 0 END
        FROM dbo.AITask WITH (UPDLOCK,HOLDLOCK) WHERE Id=? AND Status='RUNNING';
        UPDATE dbo.AITask SET Status=CASE WHEN @Retry=1 THEN 'WAITING' ELSE 'FAILED' END,
          RetryCount=CASE WHEN @Retry=1 THEN RetryCount+1 ELSE RetryCount END,
          NextRunTime=CASE WHEN @Retry=1 THEN DATEADD(MINUTE,POWER(2,RetryCount),SYSUTCDATETIME()) ELSE NextRunTime END,
          ErrorMessage=?,FinishTime=CASE WHEN @Retry=1 THEN NULL ELSE SYSUTCDATETIME() END,
          WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=?;
        IF @Retry=0 UPDATE dbo.KnowledgeDocument SET Status='FAILED',UpdateTime=SYSUTCDATETIME() WHERE Id=?;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            connection.cursor().execute(sql, task.task_id, safe_message, task.task_id, task.document_id)

    def search(self, query: str, top_k: int, document_type: str | None, embedder: HashingTextEmbedder) -> list[RagSearchItem]:
        sql = """
        SELECT d.Id,d.Code,d.Title,d.DocumentType,c.ChunkIndex,c.Content,e.VectorData
        FROM dbo.KnowledgeEmbedding e
        JOIN dbo.KnowledgeChunk c ON c.Id=e.KnowledgeChunkId
        JOIN dbo.KnowledgeDocument d ON d.Id=c.KnowledgeDocumentId
        WHERE d.Status='ACTIVE' AND d.IsDeleted=0 AND e.EmbeddingModel=? AND e.Dimension=?
          AND (? IS NULL OR d.DocumentType=?)
        """
        query_vector = embedder.embed(query)
        candidates: list[RagSearchItem] = []
        with pyodbc.connect(build_connection_string(), timeout=30) as connection:
            rows = connection.cursor().execute(sql, embedder.model_name, embedder.dimension, document_type, document_type).fetchall()
        for row in rows:
            score = embedder.cosine(query_vector, embedder.deserialize(bytes(row[6])))
            candidates.append(RagSearchItem(
                document_id=int(row[0]), document_code=str(row[1]), title=str(row[2]), document_type=str(row[3]),
                chunk_index=int(row[4]), content=str(row[5]), score=round(score, 6),
            ))
        candidates.sort(key=lambda item: (-item.score, item.document_code, item.chunk_index))
        return candidates[:top_k]
