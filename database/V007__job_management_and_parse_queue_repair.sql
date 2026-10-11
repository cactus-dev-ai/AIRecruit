/* RecruitAI V007 - repair job configs and missing RESUME_PARSE tasks. SQL Server 2008 R2. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,Version,UpdatedBy)
SELECT j.Id,40,30,20,10,1,j.CreatedBy
  FROM dbo.Job j
 WHERE j.IsDeleted=0
   AND NOT EXISTS(SELECT 1 FROM dbo.ScoreConfig s WHERE s.JobId=j.Id);

INSERT dbo.AITask(TaskType,ResumeId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey,NextRunTime,CreateTime,UpdateTime)
SELECT 'RESUME_PARSE',r.Id,'WAITING',4,0,3,
       'resume-parse:'+CONVERT(VARCHAR(30),r.Id),
       SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME()
  FROM dbo.Resume r
 WHERE r.IsDeleted=0 AND r.IsCurrent=1 AND r.IsAiAnalyzed=0
   AND EXISTS(
       SELECT 1 FROM dbo.CandidateJob cj
       JOIN dbo.Job j ON j.Id=cj.JobId AND j.IsDeleted=0
       WHERE cj.CandidateId=r.CandidateId
   )
   AND NOT EXISTS(SELECT 1 FROM dbo.AITask t WHERE t.TaskType='RESUME_PARSE' AND t.ResumeId=r.Id);

COMMIT TRANSACTION;
GO
