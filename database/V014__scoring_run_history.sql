/* RecruitAI V014 - repeatable scoring runs and immutable ranking history
   SQL Server 2008 R2 / compatibility level 100 */
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

IF OBJECT_ID(N'dbo.ScoringRun',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.ScoringRun(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScoringRun PRIMARY KEY,
  JobId BIGINT NOT NULL,
  RunNo INT NOT NULL,
  ScoreConfigVersion INT NOT NULL,
  SkillWeight DECIMAL(5,2) NOT NULL,
  ProjectWeight DECIMAL(5,2) NOT NULL,
  ExperienceWeight DECIMAL(5,2) NOT NULL,
  EducationWeight DECIMAL(5,2) NOT NULL,
  DutyWeight DECIMAL(5,2) NOT NULL,
  ProductWeight DECIMAL(5,2) NOT NULL,
  IndustryWeight DECIMAL(5,2) NOT NULL,
  TenureWeight DECIMAL(5,2) NOT NULL,
  CompanyEnvironmentWeight DECIMAL(5,2) NOT NULL,
  RuleJson NVARCHAR(MAX) NULL,
  JobName NVARCHAR(200) NOT NULL,
  JobDescription NVARCHAR(MAX) NULL,
  JobRequirement NVARCHAR(MAX) NOT NULL,
  SkillRequirement NVARCHAR(MAX) NULL,
  EducationRequirement NVARCHAR(100) NULL,
  MinExperienceMonths INT NULL,
  JobProfileDocumentId BIGINT NOT NULL,
  JobProfileVersion INT NOT NULL,
  JobProfileContent NVARCHAR(MAX) NOT NULL,
  CreatedBy BIGINT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ScoringRun_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT UQ_ScoringRun_Job_RunNo UNIQUE(JobId,RunNo),
  CONSTRAINT FK_ScoringRun_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_ScoringRun_Profile FOREIGN KEY(JobProfileDocumentId) REFERENCES dbo.KnowledgeDocument(Id),
  CONSTRAINT FK_ScoringRun_User FOREIGN KEY(CreatedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT CK_ScoringRun_RunNo CHECK(RunNo>0),
  CONSTRAINT CK_ScoringRun_ConfigVersion CHECK(ScoreConfigVersion>0 AND JobProfileVersion>0)
 );
 CREATE INDEX IX_ScoringRun_Job_Create ON dbo.ScoringRun(JobId,CreateTime DESC,Id DESC);
END;

IF OBJECT_ID(N'dbo.ScoringRunResult',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.ScoringRunResult(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScoringRunResult PRIMARY KEY,
  ScoringRunId BIGINT NOT NULL,
  CandidateJobId BIGINT NOT NULL,
  CandidateId BIGINT NOT NULL,
  ResumeId BIGINT NOT NULL,
  CandidateName NVARCHAR(100) NOT NULL,
  CurrentCompany NVARCHAR(200) NULL,
  ResumeImportTime DATETIME2(3) NOT NULL,
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_ScoringRunResult_Status DEFAULT('WAITING'),
  SkillScore DECIMAL(5,2) NULL,
  ProjectScore DECIMAL(5,2) NULL,
  ExperienceScore DECIMAL(5,2) NULL,
  EducationScore DECIMAL(5,2) NULL,
  AIScore DECIMAL(5,2) NULL,
  AIReport NVARCHAR(MAX) NULL,
  Strengths NVARCHAR(MAX) NULL,
  Weaknesses NVARCHAR(MAX) NULL,
  Recommendation NVARCHAR(1000) NULL,
  ModelName VARCHAR(100) NULL,
  PromptVersion VARCHAR(50) NULL,
  ErrorMessage NVARCHAR(2000) NULL,
  StartTime DATETIME2(3) NULL,
  FinishTime DATETIME2(3) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ScoringRunResult_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ScoringRunResult_Update DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT UQ_ScoringRunResult_Run_CandidateJob UNIQUE(ScoringRunId,CandidateJobId),
  CONSTRAINT FK_ScoringRunResult_Run FOREIGN KEY(ScoringRunId) REFERENCES dbo.ScoringRun(Id),
  CONSTRAINT CK_ScoringRunResult_Status CHECK(Status IN('WAITING','RUNNING','SUCCESS','FAILED','CANCELLED')),
  CONSTRAINT CK_ScoringRunResult_Scores CHECK(
   (SkillScore IS NULL OR SkillScore BETWEEN 0 AND 100) AND
   (ProjectScore IS NULL OR ProjectScore BETWEEN 0 AND 100) AND
   (ExperienceScore IS NULL OR ExperienceScore BETWEEN 0 AND 100) AND
   (EducationScore IS NULL OR EducationScore BETWEEN 0 AND 100) AND
   (AIScore IS NULL OR AIScore BETWEEN 0 AND 100))
 );
 CREATE INDEX IX_ScoringRunResult_Run_Status_Score ON dbo.ScoringRunResult(ScoringRunId,Status,AIScore DESC,Id);
 CREATE INDEX IX_ScoringRunResult_CandidateJob ON dbo.ScoringRunResult(CandidateJobId,ScoringRunId DESC);
END;

IF COL_LENGTH(N'dbo.AITask',N'ScoringRunId') IS NULL
 ALTER TABLE dbo.AITask ADD ScoringRunId BIGINT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AITask_ScoringRun')
 EXEC sp_executesql N'ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT FK_AITask_ScoringRun FOREIGN KEY(ScoringRunId) REFERENCES dbo.ScoringRun(Id);';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AITask') AND name=N'IX_AITask_ScoringRun_Status')
 EXEC sp_executesql N'CREATE INDEX IX_AITask_ScoringRun_Status ON dbo.AITask(ScoringRunId,Status,Id) WHERE ScoringRunId IS NOT NULL;';

/* Preserve the latest pre-V014 ranking as run 1 for every job that has scores or score tasks. */
INSERT dbo.ScoringRun(JobId,RunNo,ScoreConfigVersion,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,
 DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,RuleJson,
 JobName,JobDescription,JobRequirement,SkillRequirement,EducationRequirement,MinExperienceMonths,
 JobProfileDocumentId,JobProfileVersion,JobProfileContent,CreatedBy,CreateTime)
SELECT j.Id,1,COALESCE(scoredVersion.Version,s.Version),
 COALESCE(h.SkillWeight,s.SkillWeight),COALESCE(h.ProjectWeight,s.ProjectWeight),COALESCE(h.ExperienceWeight,s.ExperienceWeight),COALESCE(h.EducationWeight,s.EducationWeight),
 COALESCE(h.DutyWeight,s.DutyWeight),COALESCE(h.ProductWeight,s.ProductWeight),COALESCE(h.IndustryWeight,s.IndustryWeight),COALESCE(h.TenureWeight,s.TenureWeight),COALESCE(h.CompanyEnvironmentWeight,s.CompanyEnvironmentWeight),COALESCE(h.RuleJson,s.RuleJson),
 j.Name,j.Description,j.Requirement,j.SkillRequirement,j.EducationRequirement,j.MinExperienceMonths,
 d.Id,d.Version,d.Content,s.UpdatedBy,COALESCE(lastScore.UpdateTime,s.UpdateTime)
FROM dbo.Job j
JOIN dbo.ScoreConfig s ON s.JobId=j.Id
JOIN dbo.JobKnowledgeDocument x ON x.JobId=j.Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT'
JOIN dbo.KnowledgeDocument d ON d.Id=x.KnowledgeDocumentId AND d.IsDeleted=0
OUTER APPLY(SELECT TOP(1) cj.ScoreConfigVersion Version FROM dbo.CandidateJob cj WHERE cj.JobId=j.Id AND cj.AIScore IS NOT NULL AND cj.ScoreConfigVersion IS NOT NULL ORDER BY cj.ScoreConfigVersion DESC,cj.Id DESC) scoredVersion
LEFT JOIN dbo.ScoreConfigHistory h ON h.JobId=j.Id AND h.Version=scoredVersion.Version
OUTER APPLY(SELECT MAX(cj.UpdateTime) UpdateTime FROM dbo.CandidateJob cj WHERE cj.JobId=j.Id AND cj.AIScore IS NOT NULL) lastScore
WHERE NOT EXISTS(SELECT 1 FROM dbo.ScoringRun sr WHERE sr.JobId=j.Id)
 AND (EXISTS(SELECT 1 FROM dbo.CandidateJob z WHERE z.JobId=j.Id AND z.AIScore IS NOT NULL)
   OR EXISTS(SELECT 1 FROM dbo.AITask t JOIN dbo.CandidateJob z ON z.Id=t.CandidateJobId WHERE t.TaskType='MATCH_SCORE' AND z.JobId=j.Id));

INSERT dbo.ScoringRunResult(ScoringRunId,CandidateJobId,CandidateId,ResumeId,CandidateName,CurrentCompany,ResumeImportTime,
 Status,SkillScore,ProjectScore,ExperienceScore,EducationScore,AIScore,AIReport,Strengths,Weaknesses,Recommendation,
 ModelName,PromptVersion,FinishTime,CreateTime,UpdateTime)
SELECT sr.Id,cj.Id,c.Id,r.Id,c.Name,cp.CurrentCompany,r.CreateTime,'SUCCESS',
 cj.SkillScore,cj.ProjectScore,cj.ExperienceScore,cj.EducationScore,cj.AIScore,cj.AIReport,cj.Strengths,cj.Weaknesses,
 cj.Recommendation,cj.ModelName,cj.PromptVersion,cj.UpdateTime,cj.UpdateTime,cj.UpdateTime
FROM dbo.ScoringRun sr
JOIN dbo.CandidateJob cj ON cj.JobId=sr.JobId AND cj.AIScore IS NOT NULL
JOIN dbo.Candidate c ON c.Id=cj.CandidateId
JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
LEFT JOIN dbo.CandidateProfile cp ON cp.CandidateId=c.Id
WHERE sr.RunNo=1 AND NOT EXISTS(SELECT 1 FROM dbo.ScoringRunResult x WHERE x.ScoringRunId=sr.Id AND x.CandidateJobId=cj.Id);

INSERT dbo.ScoringRunResult(ScoringRunId,CandidateJobId,CandidateId,ResumeId,CandidateName,CurrentCompany,ResumeImportTime,
 Status,ErrorMessage,StartTime,FinishTime,CreateTime,UpdateTime)
SELECT sr.Id,cj.Id,c.Id,r.Id,c.Name,cp.CurrentCompany,r.CreateTime,
 CASE WHEN latest.Status='SUCCESS' THEN 'FAILED' ELSE latest.Status END,
 CASE WHEN latest.Status='SUCCESS' THEN N'旧评分任务缺少可迁移的评分结果。' ELSE latest.ErrorMessage END,
 latest.StartTime,latest.FinishTime,latest.CreateTime,latest.UpdateTime
FROM dbo.ScoringRun sr
JOIN dbo.CandidateJob cj ON cj.JobId=sr.JobId
JOIN dbo.Candidate c ON c.Id=cj.CandidateId
JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
LEFT JOIN dbo.CandidateProfile cp ON cp.CandidateId=c.Id
CROSS APPLY(SELECT TOP(1) t.Status,t.ErrorMessage,t.StartTime,t.FinishTime,t.CreateTime,t.UpdateTime
 FROM dbo.AITask t WHERE t.TaskType='MATCH_SCORE' AND t.CandidateJobId=cj.Id ORDER BY t.Id DESC) latest
WHERE sr.RunNo=1 AND NOT EXISTS(SELECT 1 FROM dbo.ScoringRunResult x WHERE x.ScoringRunId=sr.Id AND x.CandidateJobId=cj.Id);

EXEC sp_executesql N'UPDATE t SET ScoringRunId=sr.Id
 FROM dbo.AITask t
 JOIN dbo.CandidateJob cj ON cj.Id=t.CandidateJobId
 JOIN dbo.ScoringRun sr ON sr.JobId=cj.JobId AND sr.RunNo=1
 WHERE t.TaskType=''MATCH_SCORE'' AND t.ScoringRunId IS NULL;';

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @Message NVARCHAR(4000),@Severity INT,@State INT;
 SELECT @Message=ERROR_MESSAGE(),@Severity=ERROR_SEVERITY(),@State=ERROR_STATE();
 RAISERROR(@Message,@Severity,@State);
END CATCH;
GO
