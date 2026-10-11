/*
  RecruitAI V002 - Web导入、人工复核、知识Excel暂存
  目标平台: SQL Server 2008 R2, compatibility_level = 100
  说明: 只做结构变更；admin/123456 由Web启动初始化程序使用PasswordHasher写入。
*/
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

    IF COL_LENGTH('dbo.Resume', 'OriginalFileSize') IS NULL
        ALTER TABLE dbo.Resume ADD OriginalFileSize BIGINT NULL;

    IF COL_LENGTH('dbo.Resume', 'IsOptimized') IS NULL
        ALTER TABLE dbo.Resume ADD IsOptimized BIT NOT NULL
            CONSTRAINT DF_Resume_IsOptimized DEFAULT (0) WITH VALUES;

    IF COL_LENGTH('dbo.KnowledgeDocument', 'Code') IS NULL
        ALTER TABLE dbo.KnowledgeDocument ADD Code NVARCHAR(100) NULL;

    EXEC sp_executesql N'
        UPDATE dbo.KnowledgeDocument
           SET Code = N''LEGACY-'' + CONVERT(NVARCHAR(30), Id)
         WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'''';';

    IF EXISTS (
        SELECT 1 FROM sys.columns
         WHERE object_id = OBJECT_ID(N'dbo.KnowledgeDocument')
           AND name = N'Code' AND is_nullable = 1
    )
        EXEC sp_executesql N'ALTER TABLE dbo.KnowledgeDocument ALTER COLUMN Code NVARCHAR(100) NOT NULL;';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.KnowledgeDocument') AND name = N'UX_KnowledgeDocument_Code')
        EXEC sp_executesql N'CREATE UNIQUE INDEX UX_KnowledgeDocument_Code ON dbo.KnowledgeDocument(Code);';

    IF OBJECT_ID(N'dbo.ResumeImportBatch', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ResumeImportBatch (
            Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResumeImportBatch PRIMARY KEY,
            BatchKey         UNIQUEIDENTIFIER NOT NULL,
            Remark           NVARCHAR(500) NULL,
            Status           VARCHAR(20) NOT NULL CONSTRAINT DF_ResumeImportBatch_Status DEFAULT ('REVIEWING'),
            SourceFileCount  INT NOT NULL CONSTRAINT DF_ResumeImportBatch_SourceCount DEFAULT (0),
            HrUploadCount    INT NOT NULL CONSTRAINT DF_ResumeImportBatch_HrCount DEFAULT (0),
            ThirdPartyCount  INT NOT NULL CONSTRAINT DF_ResumeImportBatch_ThirdCount DEFAULT (0),
            ResumeFileCount  INT NOT NULL CONSTRAINT DF_ResumeImportBatch_ResumeCount DEFAULT (0),
            SuccessCount     INT NOT NULL CONSTRAINT DF_ResumeImportBatch_SuccessCount DEFAULT (0),
            FailedCount      INT NOT NULL CONSTRAINT DF_ResumeImportBatch_FailedCount DEFAULT (0),
            CreatedBy        BIGINT NOT NULL,
            ConfirmedBy      BIGINT NULL,
            ConfirmTime      DATETIME2(3) NULL,
            FinishTime       DATETIME2(3) NULL,
            CreateTime       DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportBatch_CreateTime DEFAULT (SYSUTCDATETIME()),
            UpdateTime       DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportBatch_UpdateTime DEFAULT (SYSUTCDATETIME()),
            RowVersion       ROWVERSION NOT NULL,
            CONSTRAINT UQ_ResumeImportBatch_BatchKey UNIQUE (BatchKey),
            CONSTRAINT FK_ResumeImportBatch_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT FK_ResumeImportBatch_ConfirmedBy FOREIGN KEY (ConfirmedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT CK_ResumeImportBatch_Status CHECK (Status IN ('REVIEWING','QUEUED','PROCESSING','SUCCESS','PARTIAL_SUCCESS','FAILED','CANCELLED')),
            CONSTRAINT CK_ResumeImportBatch_Counts CHECK (SourceFileCount >= 0 AND HrUploadCount >= 0 AND ThirdPartyCount >= 0 AND ResumeFileCount >= 0 AND SuccessCount >= 0 AND FailedCount >= 0)
        );
        CREATE INDEX IX_ResumeImportBatch_Status_CreateTime ON dbo.ResumeImportBatch(Status, CreateTime DESC);
        CREATE INDEX IX_ResumeImportBatch_CreatedBy_Status ON dbo.ResumeImportBatch(CreatedBy, Status, Id DESC);
    END;

    IF OBJECT_ID(N'dbo.ResumeImportFile', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ResumeImportFile (
            Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResumeImportFile PRIMARY KEY,
            BatchId              BIGINT NOT NULL,
            SourceType           VARCHAR(30) NOT NULL,
            OriginalFileName     NVARCHAR(260) NOT NULL,
            FileExtension        VARCHAR(10) NOT NULL,
            OriginalSourcePath   NVARCHAR(1000) NOT NULL,
            TempFilePath         NVARCHAR(1000) NOT NULL,
            FileSize             BIGINT NOT NULL,
            FileHash             CHAR(64) NOT NULL,
            Status               VARCHAR(20) NOT NULL CONSTRAINT DF_ResumeImportFile_Status DEFAULT ('REVIEW_READY'),
            DetectedResumeCount  INT NULL,
            ErrorCode            VARCHAR(100) NULL,
            ErrorMessage         NVARCHAR(2000) NULL,
            RetryCount           INT NOT NULL CONSTRAINT DF_ResumeImportFile_Retry DEFAULT (0),
            ReviewedBy           BIGINT NULL,
            ReviewTime           DATETIME2(3) NULL,
            CreateTime           DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportFile_CreateTime DEFAULT (SYSUTCDATETIME()),
            UpdateTime           DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportFile_UpdateTime DEFAULT (SYSUTCDATETIME()),
            RowVersion           ROWVERSION NOT NULL,
            CONSTRAINT FK_ResumeImportFile_Batch FOREIGN KEY (BatchId) REFERENCES dbo.ResumeImportBatch(Id),
            CONSTRAINT FK_ResumeImportFile_ReviewedBy FOREIGN KEY (ReviewedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT CK_ResumeImportFile_Source CHECK (SourceType IN ('HR_UPLOAD','THIRD_PARTY_IMPORT')),
            CONSTRAINT CK_ResumeImportFile_Extension CHECK (FileExtension IN ('PDF','DOCX')),
            CONSTRAINT CK_ResumeImportFile_Status CHECK (Status IN ('REVIEW_READY','REJECTED','QUEUED','PROCESSING','SPLIT','SUCCESS','FAILED','PURGED','RETURNED')),
            CONSTRAINT CK_ResumeImportFile_Size CHECK (FileSize > 0),
            CONSTRAINT CK_ResumeImportFile_Retry CHECK (RetryCount >= 0),
            CONSTRAINT CK_ResumeImportFile_DetectedCount CHECK (DetectedResumeCount IS NULL OR DetectedResumeCount >= 0)
        );
        CREATE INDEX IX_ResumeImportFile_Batch_Status_Id ON dbo.ResumeImportFile(BatchId, Status, Id);
        CREATE INDEX IX_ResumeImportFile_Batch_Hash ON dbo.ResumeImportFile(BatchId, FileHash);
        CREATE INDEX IX_ResumeImportFile_Status_UpdateTime ON dbo.ResumeImportFile(Status, UpdateTime);
    END;

    IF OBJECT_ID(N'dbo.ResumeImportBatchJob', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ResumeImportBatchJob (
            BatchId       BIGINT NOT NULL,
            JobId         BIGINT NOT NULL,
            CreateTime    DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportBatchJob_CreateTime DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ResumeImportBatchJob PRIMARY KEY (BatchId, JobId),
            CONSTRAINT FK_ResumeImportBatchJob_Batch FOREIGN KEY (BatchId) REFERENCES dbo.ResumeImportBatch(Id),
            CONSTRAINT FK_ResumeImportBatchJob_Job FOREIGN KEY (JobId) REFERENCES dbo.Job(Id)
        );
    END;

    IF OBJECT_ID(N'dbo.ResumeImportResult', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ResumeImportResult (
            Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResumeImportResult PRIMARY KEY,
            ImportFileId     BIGINT NOT NULL,
            ResumeId         BIGINT NOT NULL,
            SequenceNo       INT NOT NULL,
            SourcePageStart  INT NULL,
            SourcePageEnd    INT NULL,
            CreateTime       DATETIME2(3) NOT NULL CONSTRAINT DF_ResumeImportResult_CreateTime DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT FK_ResumeImportResult_File FOREIGN KEY (ImportFileId) REFERENCES dbo.ResumeImportFile(Id),
            CONSTRAINT FK_ResumeImportResult_Resume FOREIGN KEY (ResumeId) REFERENCES dbo.Resume(Id),
            CONSTRAINT UQ_ResumeImportResult_File_Sequence UNIQUE (ImportFileId, SequenceNo),
            CONSTRAINT UQ_ResumeImportResult_Resume UNIQUE (ResumeId),
            CONSTRAINT CK_ResumeImportResult_Sequence CHECK (SequenceNo > 0),
            CONSTRAINT CK_ResumeImportResult_Pages CHECK ((SourcePageStart IS NULL AND SourcePageEnd IS NULL) OR (SourcePageStart > 0 AND SourcePageEnd >= SourcePageStart))
        );
    END;

    IF OBJECT_ID(N'dbo.CandidateJobReview', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.CandidateJobReview (
            Id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CandidateJobReview PRIMARY KEY,
            CandidateJobId        BIGINT NOT NULL,
            PreviousManualScore   DECIMAL(5,2) NULL,
            NewManualScore        DECIMAL(5,2) NULL,
            PreviousStatus        VARCHAR(20) NOT NULL,
            NewStatus             VARCHAR(20) NOT NULL,
            Reason                NVARCHAR(500) NOT NULL,
            ReviewedBy            BIGINT NOT NULL,
            CreateTime            DATETIME2(3) NOT NULL CONSTRAINT DF_CandidateJobReview_CreateTime DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT FK_CandidateJobReview_CandidateJob FOREIGN KEY (CandidateJobId) REFERENCES dbo.CandidateJob(Id),
            CONSTRAINT FK_CandidateJobReview_ReviewedBy FOREIGN KEY (ReviewedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT CK_CandidateJobReview_Scores CHECK ((PreviousManualScore IS NULL OR PreviousManualScore BETWEEN 0 AND 100) AND (NewManualScore IS NULL OR NewManualScore BETWEEN 0 AND 100)),
            CONSTRAINT CK_CandidateJobReview_Status CHECK (PreviousStatus IN ('PENDING','RECOMMENDED','HOLD','INTERVIEW','REJECTED') AND NewStatus IN ('RECOMMENDED','HOLD','INTERVIEW','REJECTED')),
            CONSTRAINT CK_CandidateJobReview_Reason CHECK (LEN(LTRIM(RTRIM(Reason))) BETWEEN 1 AND 500)
        );
        CREATE INDEX IX_CandidateJobReview_CandidateJob_CreateTime ON dbo.CandidateJobReview(CandidateJobId, CreateTime DESC);
    END;

    IF OBJECT_ID(N'dbo.KnowledgeImportBatch', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.KnowledgeImportBatch (
            Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_KnowledgeImportBatch PRIMARY KEY,
            OriginalFileName NVARCHAR(260) NOT NULL,
            FileHash        CHAR(64) NOT NULL,
            Status          VARCHAR(20) NOT NULL,
            TotalCount      INT NOT NULL CONSTRAINT DF_KnowledgeImportBatch_Total DEFAULT (0),
            ReadyCount      INT NOT NULL CONSTRAINT DF_KnowledgeImportBatch_Ready DEFAULT (0),
            DuplicateCount  INT NOT NULL CONSTRAINT DF_KnowledgeImportBatch_Duplicate DEFAULT (0),
            ConflictCount   INT NOT NULL CONSTRAINT DF_KnowledgeImportBatch_Conflict DEFAULT (0),
            InvalidCount    INT NOT NULL CONSTRAINT DF_KnowledgeImportBatch_Invalid DEFAULT (0),
            CreatedBy       BIGINT NOT NULL,
            ConfirmedBy     BIGINT NULL,
            ConfirmTime     DATETIME2(3) NULL,
            CreateTime      DATETIME2(3) NOT NULL CONSTRAINT DF_KnowledgeImportBatch_CreateTime DEFAULT (SYSUTCDATETIME()),
            UpdateTime      DATETIME2(3) NOT NULL CONSTRAINT DF_KnowledgeImportBatch_UpdateTime DEFAULT (SYSUTCDATETIME()),
            RowVersion      ROWVERSION NOT NULL,
            CONSTRAINT FK_KnowledgeImportBatch_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT FK_KnowledgeImportBatch_ConfirmedBy FOREIGN KEY (ConfirmedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT CK_KnowledgeImportBatch_Status CHECK (Status IN ('PARSED','CONFLICT','READY','IMPORTED','FAILED')),
            CONSTRAINT CK_KnowledgeImportBatch_Counts CHECK (TotalCount >= 0 AND ReadyCount >= 0 AND DuplicateCount >= 0 AND ConflictCount >= 0 AND InvalidCount >= 0)
        );
        CREATE INDEX IX_KnowledgeImportBatch_Status_CreateTime ON dbo.KnowledgeImportBatch(Status, CreateTime DESC);
    END;

    IF OBJECT_ID(N'dbo.KnowledgeImportRow', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.KnowledgeImportRow (
            Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_KnowledgeImportRow PRIMARY KEY,
            BatchId              BIGINT NOT NULL,
            RowNumber            INT NOT NULL,
            Code                 NVARCHAR(100) NOT NULL,
            Title                NVARCHAR(300) NOT NULL,
            DocumentType         VARCHAR(30) NOT NULL,
            Content              NVARCHAR(MAX) NOT NULL,
            ContentHash          CHAR(64) NOT NULL,
            Status               VARCHAR(20) NOT NULL,
            ExistingDocumentId   BIGINT NULL,
            ErrorMessage         NVARCHAR(1000) NULL,
            UpdatedBy            BIGINT NULL,
            UpdateTime           DATETIME2(3) NULL,
            RowVersion           ROWVERSION NOT NULL,
            CONSTRAINT FK_KnowledgeImportRow_Batch FOREIGN KEY (BatchId) REFERENCES dbo.KnowledgeImportBatch(Id),
            CONSTRAINT FK_KnowledgeImportRow_Document FOREIGN KEY (ExistingDocumentId) REFERENCES dbo.KnowledgeDocument(Id),
            CONSTRAINT FK_KnowledgeImportRow_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.AppUser(Id),
            CONSTRAINT UQ_KnowledgeImportRow_Batch_Row UNIQUE (BatchId, RowNumber),
            CONSTRAINT CK_KnowledgeImportRow_Row CHECK (RowNumber >= 2),
            CONSTRAINT CK_KnowledgeImportRow_Type CHECK (DocumentType IN ('JOB_REQUIREMENT','TECH_STANDARD','TALENT_STANDARD','SCORE_RULE','OTHER')),
            CONSTRAINT CK_KnowledgeImportRow_Status CHECK (Status IN ('READY','DUPLICATE','CONFLICT','INVALID','IMPORTED'))
        );
        CREATE INDEX IX_KnowledgeImportRow_Batch_Status ON dbo.KnowledgeImportRow(BatchId, Status, RowNumber);
    END;

    IF COL_LENGTH('dbo.AITask', 'ResumeImportFileId') IS NULL
        ALTER TABLE dbo.AITask ADD ResumeImportFileId BIGINT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AITask_ResumeImportFile')
        EXEC sp_executesql N'ALTER TABLE dbo.AITask ADD CONSTRAINT FK_AITask_ResumeImportFile
            FOREIGN KEY (ResumeImportFileId) REFERENCES dbo.ResumeImportFile(Id);';

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_AITask_Target' AND parent_object_id = OBJECT_ID(N'dbo.AITask'))
        ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Target;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_AITask_Type' AND parent_object_id = OBJECT_ID(N'dbo.AITask'))
        ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Type;

    EXEC sp_executesql N'ALTER TABLE dbo.AITask ADD CONSTRAINT CK_AITask_Type
        CHECK (TaskType IN (''RESUME_IMPORT'',''RESUME_PARSE'',''MATCH_SCORE'',''KNOWLEDGE_EMBED''));';
    EXEC sp_executesql N'ALTER TABLE dbo.AITask ADD CONSTRAINT CK_AITask_Target CHECK (
        (TaskType = ''RESUME_IMPORT'' AND ResumeImportFileId IS NOT NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL)
        OR (TaskType = ''RESUME_PARSE'' AND ResumeImportFileId IS NULL AND ResumeId IS NOT NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL)
        OR (TaskType = ''MATCH_SCORE'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NOT NULL AND KnowledgeDocumentId IS NULL)
        OR (TaskType = ''KNOWLEDGE_EMBED'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NOT NULL)
    );';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000), @ErrorSeverity INT, @ErrorState INT;
    SELECT @ErrorMessage = ERROR_MESSAGE(), @ErrorSeverity = ERROR_SEVERITY(), @ErrorState = ERROR_STATE();
    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH;
GO

SELECT COUNT(*) AS RecruitAITableCount
FROM sys.tables
WHERE is_ms_shipped = 0;
GO
