/*
  RecruitAI V003 - 简历大模型分析标记与待分析队列
  目标平台: SQL Server 2008 R2, compatibility_level = 100
  本脚本仅更新数据库；RESUME_PARSE 的实际执行由后续程序版本提供。
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

    IF COL_LENGTH('dbo.Resume', 'IsAiAnalyzed') IS NULL
        ALTER TABLE dbo.Resume ADD IsAiAnalyzed BIT NOT NULL
            CONSTRAINT DF_Resume_IsAiAnalyzed DEFAULT (0) WITH VALUES;

    IF COL_LENGTH('dbo.Job', 'RequiresAiAnalysis') IS NULL
        ALTER TABLE dbo.Job ADD RequiresAiAnalysis BIT NOT NULL
            CONSTRAINT DF_Job_RequiresAiAnalysis DEFAULT (0) WITH VALUES;

    /* 已由大模型生成档案的历史简历，直接标记为已分析。 */
    EXEC sp_executesql N'
        UPDATE r
           SET IsAiAnalyzed = 1
          FROM dbo.Resume r
          JOIN dbo.CandidateProfile p ON p.ResumeId = r.Id
         WHERE r.IsDeleted = 0
           AND p.ModelName LIKE ''deepseek%'';';

    /* 本地 JSON 档案和其他未分析简历加入待分析队列；不重复创建任务。 */
    EXEC sp_executesql N'
        INSERT dbo.AITask(TaskType,ResumeId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey,NextRunTime,CreateTime,UpdateTime)
        SELECT ''RESUME_PARSE'',r.Id,''WAITING'',4,0,3,
               ''resume-parse:'' + CONVERT(VARCHAR(30),r.Id),
               SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME()
          FROM dbo.Resume r
         WHERE r.IsDeleted = 0
           AND r.IsAiAnalyzed = 0
           AND NOT EXISTS (
               SELECT 1 FROM dbo.AITask t
                WHERE t.TaskType = ''RESUME_PARSE''
                  AND t.ResumeId = r.Id
           );';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000), @ErrorSeverity INT, @ErrorState INT;
    SELECT @ErrorMessage = ERROR_MESSAGE(), @ErrorSeverity = ERROR_SEVERITY(), @ErrorState = ERROR_STATE();
    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH;
GO

SELECT
    SUM(CASE WHEN IsAiAnalyzed = 1 THEN 1 ELSE 0 END) AS AiAnalyzedResumeCount,
    SUM(CASE WHEN IsAiAnalyzed = 0 THEN 1 ELSE 0 END) AS PendingAiResumeCount
FROM dbo.Resume
WHERE IsDeleted = 0;
GO
