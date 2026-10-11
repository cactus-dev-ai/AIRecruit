/* RecruitAI V020 - repair manual resume identity and analysis state (SQL Server 2008 R2 compatible) */
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

IF OBJECT_ID('tempdb..#ManualIdentityRepair') IS NOT NULL DROP TABLE #ManualIdentityRepair;
CREATE TABLE #ManualIdentityRepair(ResumeId BIGINT NOT NULL PRIMARY KEY,CandidateId BIGINT NOT NULL,TalentId NVARCHAR(100) NOT NULL);

;WITH FileBase AS
(
    SELECT r.Id,r.CandidateId,r.FileName,
        LEFT(r.FileName,LEN(r.FileName)-CHARINDEX('.',REVERSE(r.FileName))) BaseName
    FROM dbo.Resume r
    WHERE r.IsDeleted=0 AND r.TalentId IS NULL AND r.ResumeSource='HR_UPLOAD'
      AND r.FileName LIKE N'51job[_]%'
      AND CHARINDEX('.',REVERSE(r.FileName))>0
), Positions AS
(
    SELECT Id,CandidateId,BaseName,
        LEN(BaseName)-CHARINDEX('(',REVERSE(BaseName))+1 OpenPos,
        LEN(BaseName)-CHARINDEX(')',REVERSE(BaseName))+1 ClosePos
    FROM FileBase
    WHERE CHARINDEX('(',REVERSE(BaseName))>0 AND CHARINDEX(')',REVERSE(BaseName))>0
), Parsed AS
(
    SELECT Id ResumeId,CandidateId,SUBSTRING(BaseName,OpenPos+1,ClosePos-OpenPos-1) TalentId
    FROM Positions WHERE ClosePos>OpenPos+1
)
INSERT #ManualIdentityRepair(ResumeId,CandidateId,TalentId)
SELECT p.ResumeId,p.CandidateId,p.TalentId
FROM Parsed p
WHERE LEN(p.TalentId) BETWEEN 5 AND 100 AND p.TalentId NOT LIKE N'%[^0-9]%'
  AND NOT EXISTS
  (
      SELECT 1 FROM dbo.Resume other
      WHERE other.IsDeleted=0 AND other.ResumeSource='51JOB' AND other.TalentId=p.TalentId
        AND other.CandidateId<>p.CandidateId
  );

UPDATE r SET TalentId=x.TalentId,ResumeSource='51JOB',UpdateTime=SYSUTCDATETIME()
FROM dbo.Resume r JOIN #ManualIdentityRepair x ON x.ResumeId=r.Id;

UPDATE c SET Source='51JOB',UpdateTime=SYSUTCDATETIME()
FROM dbo.Candidate c JOIN #ManualIdentityRepair x ON x.CandidateId=c.Id
WHERE c.Source='HR_UPLOAD';

/* Manual import already produced CandidateProfile and detail rows, so it is fully AI analyzed. */
UPDATE r SET IsAiAnalyzed=1,UpdateTime=SYSUTCDATETIME()
FROM dbo.Resume r JOIN dbo.CandidateProfile p ON p.ResumeId=r.Id AND p.CandidateId=r.CandidateId
WHERE r.IsDeleted=0 AND r.IsCurrent=1 AND r.ResumeSource IN('HR_UPLOAD','51JOB')
  AND r.ParseStatus='SUCCESS' AND r.IsAiAnalyzed=0;

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
    SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
    RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO
