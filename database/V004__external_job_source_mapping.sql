SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ExternalJobMapping',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.ExternalJobMapping(
  Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,Platform VARCHAR(30) NOT NULL,ExternalJobId NVARCHAR(100) NOT NULL,
  ExternalJobName NVARCHAR(400) NOT NULL,JobId BIGINT NOT NULL,MappingStatus VARCHAR(20) NOT NULL CONSTRAINT DF_ExternalJobMapping_Status DEFAULT('CONFIRMED'),
  CreatedBy BIGINT NULL,CreatedTime DATETIME2(3) NOT NULL CONSTRAINT DF_ExternalJobMapping_Created DEFAULT(SYSUTCDATETIME()),UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ExternalJobMapping_Updated DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT UQ_ExternalJobMapping_Platform_ExternalId UNIQUE(Platform,ExternalJobId),
  CONSTRAINT FK_ExternalJobMapping_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_ExternalJobMapping_User FOREIGN KEY(CreatedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT CK_ExternalJobMapping_Status CHECK(MappingStatus IN ('CONFIRMED','PENDING_HR'))
 );
 CREATE INDEX IX_ExternalJobMapping_Job ON dbo.ExternalJobMapping(JobId);
END;
IF COL_LENGTH('dbo.ResumeImportFile','ExternalJobMappingId') IS NULL ALTER TABLE dbo.ResumeImportFile ADD ExternalJobMappingId BIGINT NULL;
IF COL_LENGTH('dbo.ResumeImportFile','SourceJobId') IS NULL ALTER TABLE dbo.ResumeImportFile ADD SourceJobId BIGINT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_ResumeImportFile_ExternalJobMapping') ALTER TABLE dbo.ResumeImportFile ADD CONSTRAINT FK_ResumeImportFile_ExternalJobMapping FOREIGN KEY(ExternalJobMappingId) REFERENCES dbo.ExternalJobMapping(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_ResumeImportFile_SourceJob') ALTER TABLE dbo.ResumeImportFile ADD CONSTRAINT FK_ResumeImportFile_SourceJob FOREIGN KEY(SourceJobId) REFERENCES dbo.Job(Id);
IF OBJECT_ID(N'dbo.ResumeJobSource',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.ResumeJobSource(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResumeJobSource PRIMARY KEY,
  ResumeId BIGINT NOT NULL,JobId BIGINT NOT NULL,ExternalJobMappingId BIGINT NOT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeJobSource_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeJobSource_Update DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT UQ_ResumeJobSource_Resume_Mapping UNIQUE(ResumeId,ExternalJobMappingId),
  CONSTRAINT FK_ResumeJobSource_Resume FOREIGN KEY(ResumeId) REFERENCES dbo.Resume(Id),
  CONSTRAINT FK_ResumeJobSource_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_ResumeJobSource_Mapping FOREIGN KEY(ExternalJobMappingId) REFERENCES dbo.ExternalJobMapping(Id)
 );
 CREATE INDEX IX_ResumeJobSource_Job ON dbo.ResumeJobSource(JobId,ResumeId);
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
