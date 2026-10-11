/* RecruitAI V008 - AI failure handling, knowledge management and score history
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

IF OBJECT_ID(N'dbo.ScoreConfigHistory',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.ScoreConfigHistory(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScoreConfigHistory PRIMARY KEY,
  JobId BIGINT NOT NULL,
  Version INT NOT NULL,
  SkillWeight DECIMAL(5,2) NOT NULL,
  ProjectWeight DECIMAL(5,2) NOT NULL,
  ExperienceWeight DECIMAL(5,2) NOT NULL,
  EducationWeight DECIMAL(5,2) NOT NULL,
  RuleJson NVARCHAR(MAX) NULL,
  UpdatedBy BIGINT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ScoreConfigHistory_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT UQ_ScoreConfigHistory_Job_Version UNIQUE(JobId,Version),
  CONSTRAINT FK_ScoreConfigHistory_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_ScoreConfigHistory_User FOREIGN KEY(UpdatedBy) REFERENCES dbo.AppUser(Id)
 );
 CREATE INDEX IX_ScoreConfigHistory_Job ON dbo.ScoreConfigHistory(JobId,Version DESC);
END;

INSERT dbo.ScoreConfigHistory(JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,CreateTime)
SELECT s.JobId,s.Version,s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.RuleJson,s.UpdatedBy,s.UpdateTime
FROM dbo.ScoreConfig s
WHERE NOT EXISTS(SELECT 1 FROM dbo.ScoreConfigHistory h WHERE h.JobId=s.JobId AND h.Version=s.Version);

/* Knowledge remains HR-maintained, but categorized for retrieval and AI-assisted drafting. */
IF OBJECT_ID(N'dbo.CK_KnowledgeDocument_Type',N'C') IS NOT NULL
 ALTER TABLE dbo.KnowledgeDocument DROP CONSTRAINT CK_KnowledgeDocument_Type;
ALTER TABLE dbo.KnowledgeDocument WITH CHECK ADD CONSTRAINT CK_KnowledgeDocument_Type
 CHECK(DocumentType IN ('JOB_REQUIREMENT','COMPANY_INFO','INDUSTRY_INFO','TECH_STANDARD','TALENT_STANDARD','SCORE_RULE','OTHER'));

/* Old resume versions can never be claimed by the worker; close their stale tasks. */
UPDATE t SET Status='CANCELLED',ErrorMessage=N'Resume has a newer version; stale parse task cancelled.',
 FinishTime=SYSUTCDATETIME(),WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME()
FROM dbo.AITask t JOIN dbo.Resume r ON r.Id=t.ResumeId
WHERE t.TaskType='RESUME_PARSE' AND t.Status='WAITING' AND (r.IsDeleted=1 OR r.IsCurrent=0);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO
