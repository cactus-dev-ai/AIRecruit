/*
  RecruitAI V005 - independent resume parsing queue and exact-name job mapping
  Target: SQL Server 2008 R2, compatibility level 100
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    /* External ids are trace metadata only; they do not identify an internal Job. */
    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.ExternalJobMapping')
          AND name = N'UQ_ExternalJobMapping_Platform_ExternalId'
    )
        ALTER TABLE dbo.ExternalJobMapping
            DROP CONSTRAINT UQ_ExternalJobMapping_Platform_ExternalId;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ExternalJobMapping')
          AND name = N'IX_ExternalJobMapping_Platform_JobName'
    )
        CREATE INDEX IX_ExternalJobMapping_Platform_JobName
            ON dbo.ExternalJobMapping(Platform, ExternalJobName, JobId);

    /* MATCH_SCORE is intentionally disabled until its business rules mature. */
    DELETE dbo.AITask WHERE TaskType = 'MATCH_SCORE';

    /* Every current, not-yet-analyzed resume gets exactly one parse task. */
    INSERT dbo.AITask(
        TaskType, ResumeId, Status, Priority, RetryCount, MaxRetryCount,
        BusinessKey, NextRunTime, CreateTime, UpdateTime
    )
    SELECT 'RESUME_PARSE', r.Id, 'WAITING', 4, 0, 3,
           'resume-parse:' + CONVERT(VARCHAR(30), r.Id),
           SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
      FROM dbo.Resume r
     WHERE r.IsDeleted = 0
       AND r.IsCurrent = 1
       AND r.IsAiAnalyzed = 0
       AND NOT EXISTS (
           SELECT 1 FROM dbo.AITask t
            WHERE t.TaskType = 'RESUME_PARSE'
              AND t.ResumeId = r.Id
       );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000), @ErrorSeverity INT, @ErrorState INT;
    SELECT @ErrorMessage = ERROR_MESSAGE(), @ErrorSeverity = ERROR_SEVERITY(), @ErrorState = ERROR_STATE();
    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH;
GO
