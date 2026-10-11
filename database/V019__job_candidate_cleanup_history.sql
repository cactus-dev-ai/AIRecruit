IF OBJECT_ID('dbo.JobCandidateCleanupHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobCandidateCleanupHistory
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobCandidateCleanupHistory PRIMARY KEY,
        CleanupBatchId UNIQUEIDENTIFIER NOT NULL,
        JobId BIGINT NOT NULL,
        CandidateJobId BIGINT NOT NULL,
        Criterion VARCHAR(20) NOT NULL,
        PreviousStatus VARCHAR(20) NOT NULL,
        PreviousIsFavorite BIT NOT NULL,
        PreviousFavoriteTime DATETIME2 NULL,
        PreviousFavoriteBy BIGINT NULL,
        CleanupBy BIGINT NOT NULL,
        CleanupTime DATETIME2 NOT NULL CONSTRAINT DF_JobCandidateCleanupHistory_CleanupTime DEFAULT SYSUTCDATETIME(),
        IsRestored BIT NOT NULL CONSTRAINT DF_JobCandidateCleanupHistory_IsRestored DEFAULT 0,
        RestoredBy BIGINT NULL,
        RestoredTime DATETIME2 NULL,
        CONSTRAINT FK_JobCandidateCleanupHistory_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
        CONSTRAINT FK_JobCandidateCleanupHistory_CandidateJob FOREIGN KEY(CandidateJobId) REFERENCES dbo.CandidateJob(Id),
        CONSTRAINT CK_JobCandidateCleanupHistory_Criterion CHECK(Criterion IN ('AGE','SALARY'))
    );
    CREATE UNIQUE INDEX UX_JobCandidateCleanupHistory_BatchCandidate
        ON dbo.JobCandidateCleanupHistory(CleanupBatchId,CandidateJobId);
    CREATE INDEX IX_JobCandidateCleanupHistory_Restore
        ON dbo.JobCandidateCleanupHistory(JobId,IsRestored,Id);
END;
GO
