SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;

 IF COL_LENGTH(N'dbo.InterviewInvitation',N'IsDeleted') IS NULL
  ALTER TABLE dbo.InterviewInvitation ADD IsDeleted BIT NOT NULL CONSTRAINT DF_InterviewInvitation_IsDeleted DEFAULT(0);
 IF COL_LENGTH(N'dbo.InterviewInvitation',N'DeletedBy') IS NULL
  ALTER TABLE dbo.InterviewInvitation ADD DeletedBy BIGINT NULL;
 IF COL_LENGTH(N'dbo.InterviewInvitation',N'DeletedTime') IS NULL
  ALTER TABLE dbo.InterviewInvitation ADD DeletedTime DATETIME2(3) NULL;

 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_InterviewInvitation_DeletedBy')
  EXEC sp_executesql N'ALTER TABLE dbo.InterviewInvitation WITH CHECK ADD CONSTRAINT FK_InterviewInvitation_DeletedBy FOREIGN KEY(DeletedBy) REFERENCES dbo.AppUser(Id);';

 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_InterviewInvitation_Delete' AND parent_object_id=OBJECT_ID(N'dbo.InterviewInvitation'))
  EXEC sp_executesql N'ALTER TABLE dbo.InterviewInvitation WITH CHECK ADD CONSTRAINT CK_InterviewInvitation_Delete CHECK((IsDeleted=0 AND DeletedBy IS NULL AND DeletedTime IS NULL) OR (IsDeleted=1 AND DeletedBy IS NOT NULL AND DeletedTime IS NOT NULL));';

 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.InterviewInvitation') AND name=N'IX_InterviewInvitation_Visible')
  CREATE INDEX IX_InterviewInvitation_Visible ON dbo.InterviewInvitation(IsDeleted,UpdateTime DESC,Id);

 IF COL_LENGTH(N'dbo.InterviewReport',N'ResumeKeyInfo') IS NULL
  ALTER TABLE dbo.InterviewReport ADD ResumeKeyInfo NVARCHAR(MAX) NULL;
 IF COL_LENGTH(N'dbo.InterviewReport',N'VoiceInterviewInfo') IS NULL
  ALTER TABLE dbo.InterviewReport ADD VoiceInterviewInfo NVARCHAR(MAX) NULL;

 IF OBJECT_ID(N'dbo.vw_InterviewManagementList',N'V') IS NOT NULL
  DROP VIEW dbo.vw_InterviewManagementList;

 EXEC sp_executesql N'
 CREATE VIEW dbo.vw_InterviewManagementList
 AS
 SELECT
  i.Id AS InvitationId,
  i.InvitationKey,
  i.Status AS InvitationStatus,
  i.ExpireTime,
  i.VerificationMethod,
  qs.Id AS QuestionSetId,
  qs.Status AS QuestionSetStatus,
  cj.Id AS CandidateJobId,
  c.Id AS CandidateId,
  c.Name AS CandidateName,
  j.Id AS JobId,
  j.Name AS JobName,
  s.Id AS SessionId,
  s.SessionKey,
  s.Status AS SessionStatus,
  s.StartedTime,
  s.SubmittedTime,
  s.CompletedTime,
  r.Id AS ReportId,
  r.Status AS ReportStatus,
  r.OverallScore,
  r.Recommendation,
  COALESCE(s.UpdateTime,i.UpdateTime) AS LastBusinessTime
 FROM dbo.InterviewInvitation i
 INNER JOIN dbo.InterviewQuestionSet qs ON qs.Id=i.QuestionSetId
 INNER JOIN dbo.CandidateJob cj ON cj.Id=i.CandidateJobId
 INNER JOIN dbo.Candidate c ON c.Id=cj.CandidateId
 INNER JOIN dbo.Job j ON j.Id=cj.JobId
 LEFT JOIN dbo.InterviewSession s ON s.InvitationId=i.Id
 LEFT JOIN dbo.InterviewReport r ON r.SessionId=s.Id
 WHERE i.IsDeleted=0 AND c.IsDeleted=0 AND j.IsDeleted=0;';

 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO
