/* RecruitAI V006 - JSON-only resume storage (SQL Server 2008 R2). */
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

IF EXISTS (SELECT 1 FROM dbo.Resume WHERE FileExtension <> 'JSON')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR ('Resume contains a non-JSON row.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.ResumeImportFile WHERE FileExtension <> 'JSON' OR SourceType <> 'THIRD_PARTY_IMPORT')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR ('ResumeImportFile contains a non-JSON or unsupported source row.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Resume') AND name=N'CK_Resume_Extension')
    ALTER TABLE dbo.Resume DROP CONSTRAINT CK_Resume_Extension;
ALTER TABLE dbo.Resume WITH CHECK ADD CONSTRAINT CK_Resume_Extension CHECK (FileExtension='JSON');

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ResumeImportFile') AND name=N'CK_ResumeImportFile_Extension')
    ALTER TABLE dbo.ResumeImportFile DROP CONSTRAINT CK_ResumeImportFile_Extension;
ALTER TABLE dbo.ResumeImportFile WITH CHECK ADD CONSTRAINT CK_ResumeImportFile_Extension CHECK (FileExtension='JSON');

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ResumeImportFile') AND name=N'CK_ResumeImportFile_Source')
    ALTER TABLE dbo.ResumeImportFile DROP CONSTRAINT CK_ResumeImportFile_Source;
ALTER TABLE dbo.ResumeImportFile WITH CHECK ADD CONSTRAINT CK_ResumeImportFile_Source CHECK (SourceType='THIRD_PARTY_IMPORT');

COMMIT TRANSACTION;
GO
