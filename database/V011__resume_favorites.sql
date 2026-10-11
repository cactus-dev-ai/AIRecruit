/* RecruitAI V011 - shared candidate/job favorites
   SQL Server 2008 R2 / compatibility level 100 */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.CandidateJob',N'IsFavorite') IS NULL
 ALTER TABLE dbo.CandidateJob ADD IsFavorite BIT NOT NULL
  CONSTRAINT DF_CandidateJob_IsFavorite DEFAULT(0) WITH VALUES;

IF COL_LENGTH(N'dbo.CandidateJob',N'FavoriteTime') IS NULL
 ALTER TABLE dbo.CandidateJob ADD FavoriteTime DATETIME2(3) NULL;

IF COL_LENGTH(N'dbo.CandidateJob',N'FavoriteBy') IS NULL
 ALTER TABLE dbo.CandidateJob ADD FavoriteBy BIGINT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CandidateJob_FavoriteBy')
 ALTER TABLE dbo.CandidateJob WITH CHECK ADD CONSTRAINT FK_CandidateJob_FavoriteBy
  FOREIGN KEY(FavoriteBy) REFERENCES dbo.AppUser(Id);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CandidateJob') AND name=N'IX_CandidateJob_Favorite')
 CREATE INDEX IX_CandidateJob_Favorite ON dbo.CandidateJob(IsFavorite,FavoriteTime DESC,Id)
  INCLUDE(CandidateId,JobId,AIScore,ScoreConfigVersion);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO
