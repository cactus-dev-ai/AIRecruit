SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Resume_Extension' AND parent_object_id=OBJECT_ID(N'dbo.Resume'))
        ALTER TABLE dbo.Resume DROP CONSTRAINT CK_Resume_Extension;
    ALTER TABLE dbo.Resume WITH CHECK ADD CONSTRAINT CK_Resume_Extension
        CHECK(FileExtension IN ('JSON','PDF','DOCX'));

    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_ResumeImportFile_Extension' AND parent_object_id=OBJECT_ID(N'dbo.ResumeImportFile'))
        ALTER TABLE dbo.ResumeImportFile DROP CONSTRAINT CK_ResumeImportFile_Extension;
    ALTER TABLE dbo.ResumeImportFile WITH CHECK ADD CONSTRAINT CK_ResumeImportFile_Extension
        CHECK(FileExtension IN ('JSON','PDF','DOCX'));

    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_ResumeImportFile_Source' AND parent_object_id=OBJECT_ID(N'dbo.ResumeImportFile'))
        ALTER TABLE dbo.ResumeImportFile DROP CONSTRAINT CK_ResumeImportFile_Source;
    ALTER TABLE dbo.ResumeImportFile WITH CHECK ADD CONSTRAINT CK_ResumeImportFile_Source
        CHECK(SourceType IN ('THIRD_PARTY_IMPORT','HR_UPLOAD'));

    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_AITask_Target' AND parent_object_id=OBJECT_ID(N'dbo.AITask'))
        ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Target;
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_AITask_Type' AND parent_object_id=OBJECT_ID(N'dbo.AITask'))
        ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Type;

    ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT CK_AITask_Type CHECK(TaskType IN
      ('RESUME_IMPORT','MANUAL_RESUME_IMPORT','RESUME_PARSE','MATCH_SCORE','KNOWLEDGE_EMBED','INTERVIEW_QUESTION_SET','INTERVIEW_REPORT'));

    EXEC sp_executesql N'ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT CK_AITask_Target CHECK(
      (TaskType IN (''RESUME_IMPORT'',''MANUAL_RESUME_IMPORT'') AND ResumeImportFileId IS NOT NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
      OR (TaskType=''RESUME_PARSE'' AND ResumeImportFileId IS NULL AND ResumeId IS NOT NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
      OR (TaskType=''MATCH_SCORE'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NOT NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
      OR (TaskType=''KNOWLEDGE_EMBED'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NOT NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
      OR (TaskType=''INTERVIEW_QUESTION_SET'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NOT NULL AND InterviewSessionId IS NULL)
      OR (TaskType=''INTERVIEW_REPORT'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NOT NULL)
    );';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000), @ErrorSeverity INT, @ErrorState INT;
    SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
    RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO

SELECT name,definition FROM sys.check_constraints
WHERE name IN ('CK_Resume_Extension','CK_ResumeImportFile_Extension','CK_ResumeImportFile_Source','CK_AITask_Type','CK_AITask_Target')
ORDER BY name;
