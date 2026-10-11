/* RecruitAI V009 - job scoring with company/industry context
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

IF COL_LENGTH('dbo.ScoreConfig','DutyWeight') IS NULL
 ALTER TABLE dbo.ScoreConfig ADD DutyWeight DECIMAL(5,2) NOT NULL CONSTRAINT DF_ScoreConfig_DutyWeight DEFAULT(40);
IF COL_LENGTH('dbo.ScoreConfig','ProductWeight') IS NULL
 ALTER TABLE dbo.ScoreConfig ADD ProductWeight DECIMAL(5,2) NOT NULL CONSTRAINT DF_ScoreConfig_ProductWeight DEFAULT(25);
IF COL_LENGTH('dbo.ScoreConfig','IndustryWeight') IS NULL
 ALTER TABLE dbo.ScoreConfig ADD IndustryWeight DECIMAL(5,2) NOT NULL CONSTRAINT DF_ScoreConfig_IndustryWeight DEFAULT(15);
IF COL_LENGTH('dbo.ScoreConfig','TenureWeight') IS NULL
 ALTER TABLE dbo.ScoreConfig ADD TenureWeight DECIMAL(5,2) NOT NULL CONSTRAINT DF_ScoreConfig_TenureWeight DEFAULT(10);
IF COL_LENGTH('dbo.ScoreConfig','CompanyEnvironmentWeight') IS NULL
 ALTER TABLE dbo.ScoreConfig ADD CompanyEnvironmentWeight DECIMAL(5,2) NOT NULL CONSTRAINT DF_ScoreConfig_CompanyEnvironmentWeight DEFAULT(10);

IF COL_LENGTH('dbo.ScoreConfigHistory','DutyWeight') IS NULL ALTER TABLE dbo.ScoreConfigHistory ADD DutyWeight DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.ScoreConfigHistory','ProductWeight') IS NULL ALTER TABLE dbo.ScoreConfigHistory ADD ProductWeight DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.ScoreConfigHistory','IndustryWeight') IS NULL ALTER TABLE dbo.ScoreConfigHistory ADD IndustryWeight DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.ScoreConfigHistory','TenureWeight') IS NULL ALTER TABLE dbo.ScoreConfigHistory ADD TenureWeight DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.ScoreConfigHistory','CompanyEnvironmentWeight') IS NULL ALTER TABLE dbo.ScoreConfigHistory ADD CompanyEnvironmentWeight DECIMAL(5,2) NULL;

/* Dynamic SQL is required because SQL Server compiles a batch before new columns exist. */
EXEC sp_executesql N'UPDATE dbo.ScoreConfig SET SkillWeight=30,ProjectWeight=25,ExperienceWeight=35,EducationWeight=10,
 DutyWeight=40,ProductWeight=25,IndustryWeight=15,TenureWeight=10,CompanyEnvironmentWeight=10,
 Version=Version+1,UpdateTime=SYSUTCDATETIME()
 WHERE SkillWeight=40 AND ProjectWeight=30 AND ExperienceWeight=20 AND EducationWeight=10;';

EXEC sp_executesql N'UPDATE h SET DutyWeight=s.DutyWeight,ProductWeight=s.ProductWeight,IndustryWeight=s.IndustryWeight,
 TenureWeight=s.TenureWeight,CompanyEnvironmentWeight=s.CompanyEnvironmentWeight
 FROM dbo.ScoreConfigHistory h JOIN dbo.ScoreConfig s ON s.JobId=h.JobId WHERE h.DutyWeight IS NULL;
 INSERT dbo.ScoreConfigHistory(JobId,Version,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,UpdatedBy,
  DutyWeight,ProductWeight,IndustryWeight,TenureWeight,CompanyEnvironmentWeight,CreateTime)
 SELECT s.JobId,s.Version,s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.RuleJson,s.UpdatedBy,
  s.DutyWeight,s.ProductWeight,s.IndustryWeight,s.TenureWeight,s.CompanyEnvironmentWeight,s.UpdateTime
 FROM dbo.ScoreConfig s WHERE NOT EXISTS(SELECT 1 FROM dbo.ScoreConfigHistory h WHERE h.JobId=s.JobId AND h.Version=s.Version);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.CandidateJob') AND name='IX_CandidateJob_Job_AIScore')
 CREATE INDEX IX_CandidateJob_Job_AIScore ON dbo.CandidateJob(JobId,AIScore DESC,Id) INCLUDE(CandidateId,ScoreConfigVersion,Status);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @Message NVARCHAR(4000),@Severity INT,@State INT;
 SELECT @Message=ERROR_MESSAGE(),@Severity=ERROR_SEVERITY(),@State=ERROR_STATE();
 RAISERROR(@Message,@Severity,@State);
END CATCH;
GO
