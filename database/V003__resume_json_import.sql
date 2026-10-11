/* SQL Server 2008 R2 compatible: allow JSON resume source files. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.Resume',N'TalentId') IS NULL
        ALTER TABLE dbo.Resume ADD TalentId NVARCHAR(100) NULL;
    IF COL_LENGTH(N'dbo.Resume',N'ResumeSource') IS NULL
        ALTER TABLE dbo.Resume ADD ResumeSource NVARCHAR(100) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Resume') AND name=N'IX_Resume_Source_TalentId')
        EXEC(N'CREATE NONCLUSTERED INDEX IX_Resume_Source_TalentId
        ON dbo.Resume(ResumeSource,TalentId)
        WHERE TalentId IS NOT NULL AND ResumeSource IS NOT NULL AND IsDeleted=0;');

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ResumeImportFile') AND name=N'CK_ResumeImportFile_Extension')
        ALTER TABLE dbo.ResumeImportFile DROP CONSTRAINT CK_ResumeImportFile_Extension;
    ALTER TABLE dbo.ResumeImportFile WITH CHECK ADD CONSTRAINT CK_ResumeImportFile_Extension
        CHECK (FileExtension IN ('PDF','DOCX','JSON'));
    ALTER TABLE dbo.ResumeImportFile CHECK CONSTRAINT CK_ResumeImportFile_Extension;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Resume') AND name=N'CK_Resume_Extension')
        ALTER TABLE dbo.Resume DROP CONSTRAINT CK_Resume_Extension;
    ALTER TABLE dbo.Resume WITH CHECK ADD CONSTRAINT CK_Resume_Extension
        CHECK (FileExtension IN ('PDF','DOCX','JSON'));
    ALTER TABLE dbo.Resume CHECK CONSTRAINT CK_Resume_Extension;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
    SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
    RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;

SELECT c.name AS ColumnName,t.name AS DataType,c.max_length AS MaxLength
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.Resume') AND c.name IN (N'TalentId',N'ResumeSource')
ORDER BY c.column_id;
SELECT name,definition FROM sys.check_constraints
WHERE name IN ('CK_ResumeImportFile_Extension','CK_Resume_Extension') ORDER BY name;
