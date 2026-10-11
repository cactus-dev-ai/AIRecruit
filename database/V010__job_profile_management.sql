/* RecruitAI V010 - job profile ownership
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

IF OBJECT_ID(N'dbo.JobKnowledgeDocument',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.JobKnowledgeDocument(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobKnowledgeDocument PRIMARY KEY,
  JobId BIGINT NOT NULL,
  KnowledgeDocumentId BIGINT NOT NULL,
  RelationType VARCHAR(30) NOT NULL CONSTRAINT DF_JobKnowledgeDocument_Type DEFAULT('PRIMARY_REQUIREMENT'),
  IsPrimary BIT NOT NULL CONSTRAINT DF_JobKnowledgeDocument_Primary DEFAULT(1),
  Priority INT NOT NULL CONSTRAINT DF_JobKnowledgeDocument_Priority DEFAULT(100),
  CreatedBy BIGINT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_JobKnowledgeDocument_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_JobKnowledgeDocument_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_JobKnowledgeDocument_Document FOREIGN KEY(KnowledgeDocumentId) REFERENCES dbo.KnowledgeDocument(Id),
  CONSTRAINT FK_JobKnowledgeDocument_User FOREIGN KEY(CreatedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT UQ_JobKnowledgeDocument UNIQUE(JobId,KnowledgeDocumentId),
  CONSTRAINT CK_JobKnowledgeDocument_Type CHECK(RelationType IN ('PRIMARY_REQUIREMENT','SUPPLEMENT','TECH_STANDARD','TALENT_STANDARD','SCORE_RULE'))
 );
 CREATE INDEX IX_JobKnowledgeDocument_Document ON dbo.JobKnowledgeDocument(KnowledgeDocumentId,JobId);
 CREATE UNIQUE INDEX UX_JobKnowledgeDocument_Primary ON dbo.JobKnowledgeDocument(JobId)
  WHERE IsPrimary=1 AND RelationType='PRIMARY_REQUIREMENT';
END;

/* Reuse the nine published recruitment-plan documents where their titles match. */
INSERT dbo.JobKnowledgeDocument(JobId,KnowledgeDocumentId,RelationType,IsPrimary,Priority,CreatedBy)
SELECT j.Id,d.Id,'PRIMARY_REQUIREMENT',1,100,j.CreatedBy
FROM dbo.Job j
CROSS APPLY(SELECT TOP(1) k.Id FROM dbo.KnowledgeDocument k
 WHERE k.DocumentType='JOB_REQUIREMENT' AND k.IsDeleted=0
  AND (k.Title=j.Name OR k.Title=N'招聘计划：'+j.Name)
 ORDER BY CASE WHEN k.Status='ACTIVE' THEN 0 ELSE 1 END,k.Version DESC,k.Id) d
WHERE j.IsDeleted=0
 AND NOT EXISTS(SELECT 1 FROM dbo.JobKnowledgeDocument x WHERE x.JobId=j.Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT');

/* Every current job receives one editable profile, including jobs created from JSON. */
DECLARE @JobId BIGINT,@JobName NVARCHAR(200),@Requirement NVARCHAR(MAX),@CreatedBy BIGINT,@DocumentId BIGINT,@Content NVARCHAR(MAX);
DECLARE job_cursor CURSOR LOCAL FAST_FORWARD FOR
 SELECT j.Id,j.Name,j.Requirement,j.CreatedBy
 FROM dbo.Job j
 WHERE j.IsDeleted=0
  AND NOT EXISTS(SELECT 1 FROM dbo.JobKnowledgeDocument x WHERE x.JobId=j.Id AND x.IsPrimary=1 AND x.RelationType='PRIMARY_REQUIREMENT')
 ORDER BY j.Id;
OPEN job_cursor;
FETCH NEXT FROM job_cursor INTO @JobId,@JobName,@Requirement,@CreatedBy;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @Content=N'岗位名称：'+@JobName+NCHAR(13)+NCHAR(10)
  +N'岗位职责：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'核心技能：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'产品与项目经验：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'行业经验：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'学历要求：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'加分项：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'排除项：待HR完善'+NCHAR(13)+NCHAR(10)
  +N'原岗位要求：'+ISNULL(NULLIF(@Requirement,N''),N'无');
 INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
 VALUES(N'JOB-PROFILE-'+CONVERT(NVARCHAR(20),@JobId),@JobName,'JOB_REQUIREMENT',@Content,'DRAFT',
  CONVERT(CHAR(64),HASHBYTES('SHA1',CONVERT(VARBINARY(MAX),@Content)),2),1,@CreatedBy,0);
 SET @DocumentId=SCOPE_IDENTITY();
 INSERT dbo.JobKnowledgeDocument(JobId,KnowledgeDocumentId,RelationType,IsPrimary,Priority,CreatedBy)
 VALUES(@JobId,@DocumentId,'PRIMARY_REQUIREMENT',1,100,@CreatedBy);
 FETCH NEXT FROM job_cursor INTO @JobId,@JobName,@Requirement,@CreatedBy;
END;
CLOSE job_cursor;
DEALLOCATE job_cursor;

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO
