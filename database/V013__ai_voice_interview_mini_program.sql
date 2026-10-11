/* RecruitAI V013 - AI voice interview for WeChat Mini Program
   Target: SQL Server 2008 R2 / compatibility level 100

   Security conventions implemented by application code:
   - FallbackCodeVerifier = slow salted verifier; FallbackCodeLookupHash = HMAC-SHA256(code, server pepper).
   - TokenHash/OpenIdHash/UnionIdHash/PhoneHash/IP and device hashes are keyed SHA-256 digests.
   - Plain fallback codes, WeChat codes, full phone numbers and access tokens are never persisted here.
   - JSON columns are NVARCHAR(MAX) and are validated by application code; SQL 2008 R2 JSON functions are not used.
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

/* Required baseline: V001, V002 and V012 must already be present. */
IF OBJECT_ID(N'dbo.Job',N'U') IS NULL OR OBJECT_ID(N'dbo.Candidate',N'U') IS NULL
 OR OBJECT_ID(N'dbo.CandidateJob',N'U') IS NULL OR OBJECT_ID(N'dbo.Resume',N'U') IS NULL
 OR OBJECT_ID(N'dbo.AppUser',N'U') IS NULL OR OBJECT_ID(N'dbo.KnowledgeDocument',N'U') IS NULL
 OR OBJECT_ID(N'dbo.AITask',N'U') IS NULL
 RAISERROR(N'V013 prerequisite failed: RecruitAI baseline tables are missing.',16,1);

IF COL_LENGTH(N'dbo.AITask',N'ResumeImportFileId') IS NULL
 RAISERROR(N'V013 prerequisite failed: V002 AITask.ResumeImportFileId is missing.',16,1);

IF OBJECT_ID(N'dbo.InterviewPlan',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewPlan(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewPlan PRIMARY KEY,
  JobId BIGINT NOT NULL,
  Name NVARCHAR(200) NOT NULL,
  VersionNo INT NOT NULL,
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewPlan_Status DEFAULT('DRAFT'),
  DurationMinutes INT NOT NULL CONSTRAINT DF_InterviewPlan_Duration DEFAULT(20),
  MaxSessionMinutes INT NOT NULL CONSTRAINT DF_InterviewPlan_MaxDuration DEFAULT(30),
  ScoreRuleSnapshotJson NVARCHAR(MAX) NULL,
  ConsentTextVersion VARCHAR(50) NOT NULL,
  CreatedBy BIGINT NOT NULL,
  PublishedBy BIGINT NULL,
  PublishTime DATETIME2(3) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewPlan_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewPlan_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewPlan_Job FOREIGN KEY(JobId) REFERENCES dbo.Job(Id),
  CONSTRAINT FK_InterviewPlan_CreatedBy FOREIGN KEY(CreatedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT FK_InterviewPlan_PublishedBy FOREIGN KEY(PublishedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT UQ_InterviewPlan_Job_Name_Version UNIQUE(JobId,Name,VersionNo),
  CONSTRAINT CK_InterviewPlan_Status CHECK(Status IN ('DRAFT','ACTIVE','ARCHIVED')),
  CONSTRAINT CK_InterviewPlan_Version CHECK(VersionNo>0),
  CONSTRAINT CK_InterviewPlan_Duration CHECK(DurationMinutes BETWEEN 1 AND 120 AND MaxSessionMinutes BETWEEN DurationMinutes AND 180),
  CONSTRAINT CK_InterviewPlan_Publish CHECK((Status='DRAFT' AND PublishedBy IS NULL AND PublishTime IS NULL) OR (Status IN ('ACTIVE','ARCHIVED') AND PublishedBy IS NOT NULL AND PublishTime IS NOT NULL))
 );
 CREATE INDEX IX_InterviewPlan_Job_Status ON dbo.InterviewPlan(JobId,Status,VersionNo DESC);
 CREATE UNIQUE INDEX UX_InterviewPlan_Active ON dbo.InterviewPlan(JobId)
  WHERE Status='ACTIVE';
END;

IF OBJECT_ID(N'dbo.InterviewQuestion',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewQuestion(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewQuestion PRIMARY KEY,
  PlanId BIGINT NOT NULL,
  SequenceNo INT NOT NULL,
  QuestionType VARCHAR(30) NOT NULL,
  QuestionText NVARCHAR(2000) NOT NULL,
  DimensionCode VARCHAR(50) NULL,
  MaxAnswerSeconds INT NOT NULL CONSTRAINT DF_InterviewQuestion_MaxAnswer DEFAULT(120),
  MinTranscriptChars INT NOT NULL CONSTRAINT DF_InterviewQuestion_MinChars DEFAULT(5),
  FollowUpPolicyJson NVARCHAR(MAX) NULL,
  IsScored BIT NOT NULL CONSTRAINT DF_InterviewQuestion_Scored DEFAULT(1),
  IsRequired BIT NOT NULL CONSTRAINT DF_InterviewQuestion_Required DEFAULT(1),
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewQuestion_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewQuestion_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewQuestion_Plan FOREIGN KEY(PlanId) REFERENCES dbo.InterviewPlan(Id),
  CONSTRAINT UQ_InterviewQuestion_Plan_Sequence UNIQUE(PlanId,SequenceNo),
  CONSTRAINT CK_InterviewQuestion_Sequence CHECK(SequenceNo>0),
  CONSTRAINT CK_InterviewQuestion_Type CHECK(QuestionType IN ('INTRO','FIXED','CONDITION','FOLLOW_UP','CANDIDATE_QA','END')),
  CONSTRAINT CK_InterviewQuestion_Text CHECK(LEN(LTRIM(RTRIM(QuestionText))) BETWEEN 1 AND 2000),
  CONSTRAINT CK_InterviewQuestion_Answer CHECK(MaxAnswerSeconds BETWEEN 5 AND 1800 AND MinTranscriptChars BETWEEN 0 AND 4000)
 );
END;

IF OBJECT_ID(N'dbo.InterviewQuestionSet',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewQuestionSet(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewQuestionSet PRIMARY KEY,
  QuestionSetKey UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_InterviewQuestionSet_Key DEFAULT(NEWSEQUENTIALID()),
  CandidateJobId BIGINT NOT NULL,
  PlanId BIGINT NOT NULL,
  RevisionNo INT NOT NULL CONSTRAINT DF_InterviewQuestionSet_Revision DEFAULT(1),
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewQuestionSet_Status DEFAULT('GENERATING'),
  ResumeId BIGINT NOT NULL,
  ResumeProfileVersion VARCHAR(100) NOT NULL,
  CandidateJobAnalysisVersion VARCHAR(200) NOT NULL,
  JobProfileDocumentId BIGINT NULL,
  JobProfileVersion INT NULL,
  ScoreConfigVersion INT NOT NULL,
  InputVersionHash CHAR(64) NOT NULL,
  QuestionSetHash CHAR(64) NULL,
  GeneratedByModel VARCHAR(100) NULL,
  PromptVersion VARCHAR(50) NULL,
  ConfirmedBy BIGINT NULL,
  ConfirmedTime DATETIME2(3) NULL,
  ErrorCode VARCHAR(100) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewQuestionSet_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewQuestionSet_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewQuestionSet_CandidateJob FOREIGN KEY(CandidateJobId) REFERENCES dbo.CandidateJob(Id),
  CONSTRAINT FK_InterviewQuestionSet_Plan FOREIGN KEY(PlanId) REFERENCES dbo.InterviewPlan(Id),
  CONSTRAINT FK_InterviewQuestionSet_Resume FOREIGN KEY(ResumeId) REFERENCES dbo.Resume(Id),
  CONSTRAINT FK_InterviewQuestionSet_JobProfile FOREIGN KEY(JobProfileDocumentId) REFERENCES dbo.KnowledgeDocument(Id),
  CONSTRAINT FK_InterviewQuestionSet_ConfirmedBy FOREIGN KEY(ConfirmedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT UQ_InterviewQuestionSet_Key UNIQUE(QuestionSetKey),
  CONSTRAINT UQ_InterviewQuestionSet_Identity UNIQUE(Id,CandidateJobId,PlanId),
  CONSTRAINT UQ_InterviewQuestionSet_Input_Revision UNIQUE(CandidateJobId,PlanId,InputVersionHash,RevisionNo),
  CONSTRAINT CK_InterviewQuestionSet_Status CHECK(Status IN ('GENERATING','DRAFT','CONFIRMED','STALE','ARCHIVED','FAILED')),
  CONSTRAINT CK_InterviewQuestionSet_Revision CHECK(RevisionNo>0 AND ScoreConfigVersion>0),
  CONSTRAINT CK_InterviewQuestionSet_Hash CHECK(InputVersionHash NOT LIKE '%[^0-9A-Fa-f]%' AND (QuestionSetHash IS NULL OR QuestionSetHash NOT LIKE '%[^0-9A-Fa-f]%')),
  CONSTRAINT CK_InterviewQuestionSet_Confirm CHECK((Status='CONFIRMED' AND ConfirmedBy IS NOT NULL AND ConfirmedTime IS NOT NULL AND QuestionSetHash IS NOT NULL) OR Status<>'CONFIRMED')
 );
 CREATE INDEX IX_InterviewQuestionSet_CandidateJob ON dbo.InterviewQuestionSet(CandidateJobId,Status,CreateTime DESC);
 CREATE INDEX IX_InterviewQuestionSet_Plan ON dbo.InterviewQuestionSet(PlanId,Status,Id);
END;

IF OBJECT_ID(N'dbo.InterviewSessionQuestion',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewSessionQuestion(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewSessionQuestion PRIMARY KEY,
  QuestionSetId BIGINT NOT NULL,
  TemplateQuestionId BIGINT NULL,
  SequenceNo INT NOT NULL,
  QuestionType VARCHAR(30) NOT NULL,
  QuestionText NVARCHAR(2000) NOT NULL,
  DimensionCode VARCHAR(50) NULL,
  GenerationReason NVARCHAR(1000) NULL,
  ExpectedEvidenceJson NVARCHAR(MAX) NULL,
  MaxAnswerSeconds INT NOT NULL CONSTRAINT DF_InterviewSessionQuestion_MaxAnswer DEFAULT(120),
  IsRequired BIT NOT NULL CONSTRAINT DF_InterviewSessionQuestion_Required DEFAULT(1),
  IsScored BIT NOT NULL CONSTRAINT DF_InterviewSessionQuestion_Scored DEFAULT(1),
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewSessionQuestion_Status DEFAULT('DRAFT'),
  GeneratedByModel VARCHAR(100) NULL,
  PromptVersion VARCHAR(50) NULL,
  ConfirmedBy BIGINT NULL,
  ConfirmedTime DATETIME2(3) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewSessionQuestion_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewSessionQuestion_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewSessionQuestion_Set FOREIGN KEY(QuestionSetId) REFERENCES dbo.InterviewQuestionSet(Id),
  CONSTRAINT FK_InterviewSessionQuestion_Template FOREIGN KEY(TemplateQuestionId) REFERENCES dbo.InterviewQuestion(Id),
  CONSTRAINT FK_InterviewSessionQuestion_ConfirmedBy FOREIGN KEY(ConfirmedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT UQ_InterviewSessionQuestion_Set_Sequence UNIQUE(QuestionSetId,SequenceNo),
  CONSTRAINT CK_InterviewSessionQuestion_Sequence CHECK(SequenceNo>0),
  CONSTRAINT CK_InterviewSessionQuestion_Type CHECK(QuestionType IN ('FIXED','RESUME_VERIFY','GAP_PROBE','KNOWLEDGE_PROBE','FOLLOW_UP','CANDIDATE_QA','END')),
  CONSTRAINT CK_InterviewSessionQuestion_Status CHECK(Status IN ('DRAFT','CONFIRMED','ARCHIVED')),
  CONSTRAINT CK_InterviewSessionQuestion_Text CHECK(LEN(LTRIM(RTRIM(QuestionText))) BETWEEN 1 AND 2000),
  CONSTRAINT CK_InterviewSessionQuestion_Answer CHECK(MaxAnswerSeconds BETWEEN 5 AND 1800),
  CONSTRAINT CK_InterviewSessionQuestion_Confirm CHECK((Status='CONFIRMED' AND ConfirmedBy IS NOT NULL AND ConfirmedTime IS NOT NULL) OR Status<>'CONFIRMED')
 );
END;

IF OBJECT_ID(N'dbo.InterviewQuestionSource',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewQuestionSource(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewQuestionSource PRIMARY KEY,
  SessionQuestionId BIGINT NOT NULL,
  SourceType VARCHAR(30) NOT NULL,
  SourceEntityId BIGINT NULL,
  SourceVersion VARCHAR(100) NULL,
  SourceLocatorJson NVARCHAR(MAX) NULL,
  EvidenceText NVARCHAR(2000) NOT NULL,
  ContentHash CHAR(64) NOT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewQuestionSource_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_InterviewQuestionSource_Question FOREIGN KEY(SessionQuestionId) REFERENCES dbo.InterviewSessionQuestion(Id),
  CONSTRAINT CK_InterviewQuestionSource_Type CHECK(SourceType IN ('RESUME_PROFILE','MATCH_ANALYSIS','JOB_PROFILE','KNOWLEDGE_CHUNK','SCORE_RULE','HR_TEMPLATE')),
  CONSTRAINT CK_InterviewQuestionSource_Evidence CHECK(LEN(LTRIM(RTRIM(EvidenceText))) BETWEEN 1 AND 2000),
  CONSTRAINT CK_InterviewQuestionSource_Hash CHECK(ContentHash NOT LIKE '%[^0-9A-Fa-f]%')
 );
 CREATE INDEX IX_InterviewQuestionSource_Question ON dbo.InterviewQuestionSource(SessionQuestionId,SourceType,Id);
END;

IF OBJECT_ID(N'dbo.InterviewInvitation',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewInvitation(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewInvitation PRIMARY KEY,
  InvitationKey UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_InterviewInvitation_Key DEFAULT(NEWSEQUENTIALID()),
  CandidateJobId BIGINT NOT NULL,
  PlanId BIGINT NOT NULL,
  QuestionSetId BIGINT NOT NULL,
  FallbackCodeVerifier BINARY(32) NOT NULL,
  FallbackCodeSalt BINARY(32) NOT NULL,
  FallbackCodeLookupHash BINARY(32) NOT NULL,
  FallbackCodeVersion INT NOT NULL CONSTRAINT DF_InterviewInvitation_CodeVersion DEFAULT(1),
  FallbackCodeFailedCount INT NOT NULL CONSTRAINT DF_InterviewInvitation_Failed DEFAULT(0),
  FallbackCodeLockedTime DATETIME2(3) NULL,
  FallbackCodeLastFailedTime DATETIME2(3) NULL,
  FallbackCodeUsedTime DATETIME2(3) NULL,
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewInvitation_Status DEFAULT('CREATED'),
  DeliveryChannel VARCHAR(30) NOT NULL CONSTRAINT DF_InterviewInvitation_Channel DEFAULT('MANUAL'),
  VerificationMethod VARCHAR(30) NULL,
  MaskedDestination NVARCHAR(100) NULL,
  ExpireTime DATETIME2(3) NOT NULL,
  NotifiedTime DATETIME2(3) NULL,
  OpenedTime DATETIME2(3) NULL,
  VerifiedTime DATETIME2(3) NULL,
  BoundTime DATETIME2(3) NULL,
  RevokeTime DATETIME2(3) NULL,
  CreatedBy BIGINT NOT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewInvitation_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewInvitation_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewInvitation_CandidateJob FOREIGN KEY(CandidateJobId) REFERENCES dbo.CandidateJob(Id),
  CONSTRAINT FK_InterviewInvitation_Plan FOREIGN KEY(PlanId) REFERENCES dbo.InterviewPlan(Id),
  CONSTRAINT FK_InterviewInvitation_QuestionSet FOREIGN KEY(QuestionSetId) REFERENCES dbo.InterviewQuestionSet(Id),
  CONSTRAINT FK_InterviewInvitation_SetBinding FOREIGN KEY(QuestionSetId,CandidateJobId,PlanId) REFERENCES dbo.InterviewQuestionSet(Id,CandidateJobId,PlanId),
  CONSTRAINT FK_InterviewInvitation_CreatedBy FOREIGN KEY(CreatedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT UQ_InterviewInvitation_Key UNIQUE(InvitationKey),
  CONSTRAINT UQ_InterviewInvitation_Identity UNIQUE(Id,CandidateJobId,PlanId,QuestionSetId),
  CONSTRAINT CK_InterviewInvitation_Status CHECK(Status IN ('CREATED','NOTIFIED','OPENED','VERIFIED','BOUND','REVOKED','EXPIRED')),
  CONSTRAINT CK_InterviewInvitation_Channel CHECK(DeliveryChannel IN ('MANUAL','SMS','EMAIL','RECRUITMENT_PLATFORM')),
  CONSTRAINT CK_InterviewInvitation_Verification CHECK(VerificationMethod IS NULL OR VerificationMethod IN ('WECHAT_PHONE','FALLBACK_CODE','MANUAL_REVIEW')),
  CONSTRAINT CK_InterviewInvitation_Code CHECK(FallbackCodeVersion>0 AND FallbackCodeFailedCount>=0),
  CONSTRAINT CK_InterviewInvitation_Expire CHECK(ExpireTime>CreateTime),
  CONSTRAINT CK_InterviewInvitation_Times CHECK((Status NOT IN ('NOTIFIED','OPENED','VERIFIED','BOUND') OR NotifiedTime IS NOT NULL) AND (Status NOT IN ('OPENED','VERIFIED','BOUND') OR OpenedTime IS NOT NULL) AND (Status NOT IN ('VERIFIED','BOUND') OR VerifiedTime IS NOT NULL) AND (Status<>'BOUND' OR BoundTime IS NOT NULL) AND (Status<>'REVOKED' OR RevokeTime IS NOT NULL))
 );
 CREATE INDEX IX_InterviewInvitation_CandidateJob ON dbo.InterviewInvitation(CandidateJobId,Status,CreateTime DESC);
 CREATE INDEX IX_InterviewInvitation_Status_Expire ON dbo.InterviewInvitation(Status,ExpireTime,Id);
 CREATE INDEX IX_InterviewInvitation_FallbackLookup ON dbo.InterviewInvitation(FallbackCodeLookupHash,Status,ExpireTime);
 CREATE INDEX IX_InterviewInvitation_QuestionSet ON dbo.InterviewInvitation(QuestionSetId,Status,Id);
END;

IF OBJECT_ID(N'dbo.InterviewWechatIdentity',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewWechatIdentity(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewWechatIdentity PRIMARY KEY,
  InvitationId BIGINT NOT NULL,
  MiniProgramAppId VARCHAR(64) NOT NULL,
  OpenIdHash BINARY(32) NOT NULL,
  UnionIdHash BINARY(32) NULL,
  PhoneHash BINARY(32) NULL,
  PhoneLast4 CHAR(4) NULL,
  VerificationMethod VARCHAR(30) NOT NULL,
  PhoneMatchStatus VARCHAR(20) NOT NULL,
  VerifiedTime DATETIME2(3) NOT NULL,
  LastLoginTime DATETIME2(3) NOT NULL,
  ClientInfoJson NVARCHAR(MAX) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewWechatIdentity_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewWechatIdentity_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewWechatIdentity_Invitation FOREIGN KEY(InvitationId) REFERENCES dbo.InterviewInvitation(Id),
  CONSTRAINT UQ_InterviewWechatIdentity_Invitation UNIQUE(InvitationId),
  CONSTRAINT CK_InterviewWechatIdentity_Method CHECK(VerificationMethod IN ('WECHAT_PHONE','FALLBACK_CODE','MANUAL_REVIEW')),
  CONSTRAINT CK_InterviewWechatIdentity_Match CHECK(PhoneMatchStatus IN ('MATCHED','MISMATCHED','NOT_AVAILABLE','NOT_REQUIRED')),
  CONSTRAINT CK_InterviewWechatIdentity_PhoneLast4 CHECK(PhoneLast4 IS NULL OR PhoneLast4 NOT LIKE '%[^0-9]%')
 );
 CREATE INDEX IX_InterviewWechatIdentity_OpenId ON dbo.InterviewWechatIdentity(MiniProgramAppId,OpenIdHash,LastLoginTime DESC);
 CREATE INDEX IX_InterviewWechatIdentity_Phone ON dbo.InterviewWechatIdentity(PhoneHash,VerifiedTime DESC) WHERE PhoneHash IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.InterviewAccessSession',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewAccessSession(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewAccessSession PRIMARY KEY,
  AccessSessionKey UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_InterviewAccessSession_Key DEFAULT(NEWSEQUENTIALID()),
  TokenHash BINARY(32) NOT NULL,
  MiniProgramAppId VARCHAR(64) NOT NULL,
  OpenIdHash BINARY(32) NOT NULL,
  WechatIdentityId BIGINT NULL,
  InvitationId BIGINT NULL,
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewAccessSession_Status DEFAULT('TEMPORARY'),
  ExpiresTime DATETIME2(3) NOT NULL,
  BoundTime DATETIME2(3) NULL,
  RevokedTime DATETIME2(3) NULL,
  LastSeenTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAccessSession_LastSeen DEFAULT(SYSUTCDATETIME()),
  IpHash BINARY(32) NULL,
  DeviceHash BINARY(32) NULL,
  ClientInfoJson NVARCHAR(MAX) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAccessSession_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAccessSession_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewAccessSession_Identity FOREIGN KEY(WechatIdentityId) REFERENCES dbo.InterviewWechatIdentity(Id),
  CONSTRAINT FK_InterviewAccessSession_Invitation FOREIGN KEY(InvitationId) REFERENCES dbo.InterviewInvitation(Id),
  CONSTRAINT UQ_InterviewAccessSession_Key UNIQUE(AccessSessionKey),
  CONSTRAINT UQ_InterviewAccessSession_Token UNIQUE(TokenHash),
  CONSTRAINT CK_InterviewAccessSession_Status CHECK(Status IN ('TEMPORARY','BOUND','EXPIRED','REVOKED')),
  CONSTRAINT CK_InterviewAccessSession_Expire CHECK(ExpiresTime>CreateTime),
  CONSTRAINT CK_InterviewAccessSession_Binding CHECK((Status='TEMPORARY' AND WechatIdentityId IS NULL AND InvitationId IS NULL AND BoundTime IS NULL) OR (Status='BOUND' AND WechatIdentityId IS NOT NULL AND InvitationId IS NOT NULL AND BoundTime IS NOT NULL) OR Status='EXPIRED' OR (Status='REVOKED' AND RevokedTime IS NOT NULL))
 );
 CREATE INDEX IX_InterviewAccessSession_OpenId ON dbo.InterviewAccessSession(MiniProgramAppId,OpenIdHash,Status,ExpiresTime);
 CREATE INDEX IX_InterviewAccessSession_Invitation ON dbo.InterviewAccessSession(InvitationId,Status,ExpiresTime) WHERE InvitationId IS NOT NULL;
 CREATE INDEX IX_InterviewAccessSession_Expiry ON dbo.InterviewAccessSession(Status,ExpiresTime,Id);
END;

IF OBJECT_ID(N'dbo.InterviewSession',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewSession(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewSession PRIMARY KEY,
  SessionKey UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_InterviewSession_Key DEFAULT(NEWSEQUENTIALID()),
  InvitationId BIGINT NOT NULL,
  CandidateJobId BIGINT NOT NULL,
  PlanId BIGINT NOT NULL,
  QuestionSetId BIGINT NOT NULL,
  RetakeNo INT NOT NULL CONSTRAINT DF_InterviewSession_Retake DEFAULT(1),
  Status VARCHAR(30) NOT NULL CONSTRAINT DF_InterviewSession_Status DEFAULT('NOT_STARTED'),
  CurrentSequenceNo INT NOT NULL CONSTRAINT DF_InterviewSession_Sequence DEFAULT(0),
  StartedTime DATETIME2(3) NULL,
  SubmittedTime DATETIME2(3) NULL,
  CompletedTime DATETIME2(3) NULL,
  LastHeartbeatTime DATETIME2(3) NULL,
  ClientInfoJson NVARCHAR(MAX) NULL,
  DeviceCheckJson NVARCHAR(MAX) NULL,
  AsrProvider VARCHAR(50) NULL,
  TtsProvider VARCHAR(50) NULL,
  LlmProvider VARCHAR(50) NULL,
  AsrModel VARCHAR(100) NULL,
  TtsModel VARCHAR(100) NULL,
  LlmModel VARCHAR(100) NULL,
  PromptVersion VARCHAR(50) NULL,
  QuestionSetHash CHAR(64) NOT NULL,
  ErrorCode VARCHAR(100) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewSession_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewSession_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewSession_Invitation FOREIGN KEY(InvitationId) REFERENCES dbo.InterviewInvitation(Id),
  CONSTRAINT FK_InterviewSession_CandidateJob FOREIGN KEY(CandidateJobId) REFERENCES dbo.CandidateJob(Id),
  CONSTRAINT FK_InterviewSession_Plan FOREIGN KEY(PlanId) REFERENCES dbo.InterviewPlan(Id),
  CONSTRAINT FK_InterviewSession_QuestionSet FOREIGN KEY(QuestionSetId) REFERENCES dbo.InterviewQuestionSet(Id),
  CONSTRAINT FK_InterviewSession_InvitationBinding FOREIGN KEY(InvitationId,CandidateJobId,PlanId,QuestionSetId) REFERENCES dbo.InterviewInvitation(Id,CandidateJobId,PlanId,QuestionSetId),
  CONSTRAINT UQ_InterviewSession_Key UNIQUE(SessionKey),
  CONSTRAINT UQ_InterviewSession_Invitation_Retake UNIQUE(InvitationId,RetakeNo),
  CONSTRAINT CK_InterviewSession_Status CHECK(Status IN ('NOT_STARTED','DEVICE_CHECK','IN_PROGRESS','PAUSED','SUBMITTED','PROCESSING','COMPLETED','REVIEW_REQUIRED','EXPIRED','ABORTED')),
  CONSTRAINT CK_InterviewSession_Sequence CHECK(RetakeNo>0 AND CurrentSequenceNo>=0),
  CONSTRAINT CK_InterviewSession_Hash CHECK(QuestionSetHash NOT LIKE '%[^0-9A-Fa-f]%'),
  CONSTRAINT CK_InterviewSession_Times CHECK((Status NOT IN ('IN_PROGRESS','PAUSED','SUBMITTED','PROCESSING','COMPLETED','REVIEW_REQUIRED') OR StartedTime IS NOT NULL) AND (Status NOT IN ('SUBMITTED','PROCESSING','COMPLETED','REVIEW_REQUIRED') OR SubmittedTime IS NOT NULL) AND (Status<>'COMPLETED' OR CompletedTime IS NOT NULL))
 );
 CREATE INDEX IX_InterviewSession_CandidateJob ON dbo.InterviewSession(CandidateJobId,Status,CreateTime DESC);
 CREATE INDEX IX_InterviewSession_Status_Update ON dbo.InterviewSession(Status,UpdateTime DESC,Id);
 CREATE INDEX IX_InterviewSession_QuestionSet ON dbo.InterviewSession(QuestionSetId,Status,Id);
END;

IF OBJECT_ID(N'dbo.InterviewConsent',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewConsent(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewConsent PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  ConsentType VARCHAR(30) NOT NULL,
  TextVersion VARCHAR(50) NOT NULL,
  Granted BIT NOT NULL,
  GrantedTime DATETIME2(3) NOT NULL,
  WithdrawnTime DATETIME2(3) NULL,
  IpHash BINARY(32) NULL,
  ClientInfo NVARCHAR(500) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewConsent_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_InterviewConsent_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT UQ_InterviewConsent_Session_Type_Version UNIQUE(SessionId,ConsentType,TextVersion),
  CONSTRAINT CK_InterviewConsent_Type CHECK(ConsentType IN ('AI_INTERVIEW','AUDIO_RECORDING','THIRD_PARTY_PROCESSING')),
  CONSTRAINT CK_InterviewConsent_Withdraw CHECK(WithdrawnTime IS NULL OR WithdrawnTime>=GrantedTime)
 );
END;

IF OBJECT_ID(N'dbo.InterviewAsset',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewAsset(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewAsset PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  AssetType VARCHAR(30) NOT NULL,
  StorageProvider VARCHAR(30) NOT NULL,
  ProviderFileId NVARCHAR(500) NULL,
  ProviderFileHash BINARY(32) NULL,
  ObjectKey NVARCHAR(1000) NULL,
  RelativePath NVARCHAR(1000) NULL,
  RecordingMode VARCHAR(30) NULL,
  DeliveryStatus VARCHAR(30) NOT NULL CONSTRAINT DF_InterviewAsset_Delivery DEFAULT('WAITING'),
  MimeType VARCHAR(100) NULL,
  FileExtension VARCHAR(10) NULL,
  FileSize BIGINT NULL,
  Sha256 BINARY(32) NULL,
  DurationMs INT NULL,
  SampleRate INT NULL,
  Channels INT NULL,
  RetainUntil DATETIME2(3) NOT NULL,
  IsDeleted BIT NOT NULL CONSTRAINT DF_InterviewAsset_Deleted DEFAULT(0),
  DeletedTime DATETIME2(3) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAsset_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAsset_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewAsset_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT CK_InterviewAsset_Type CHECK(AssetType IN ('CANDIDATE_RECORDING','EXPORT','TEMPORARY')),
  CONSTRAINT CK_InterviewAsset_Storage CHECK(StorageProvider IN ('TENCENT_COS','TENCENT_VOD','LOCAL')),
  CONSTRAINT CK_InterviewAsset_Mode CHECK(RecordingMode IS NULL OR RecordingMode='CANDIDATE_SINGLE'),
  CONSTRAINT CK_InterviewAsset_Delivery CHECK(DeliveryStatus IN ('WAITING','SUCCESS','FAILED','DELETED')),
  CONSTRAINT CK_InterviewAsset_File CHECK((FileSize IS NULL OR FileSize>=0) AND (DurationMs IS NULL OR DurationMs>=0) AND (SampleRate IS NULL OR SampleRate>0) AND (Channels IS NULL OR Channels BETWEEN 1 AND 8)),
  CONSTRAINT CK_InterviewAsset_Location CHECK(ProviderFileId IS NOT NULL OR ObjectKey IS NOT NULL OR RelativePath IS NOT NULL OR DeliveryStatus IN ('WAITING','FAILED')),
  CONSTRAINT CK_InterviewAsset_ProviderHash CHECK((ProviderFileId IS NULL AND ProviderFileHash IS NULL) OR (ProviderFileId IS NOT NULL AND ProviderFileHash IS NOT NULL)),
  CONSTRAINT CK_InterviewAsset_Delete CHECK((IsDeleted=0 AND DeletedTime IS NULL) OR (IsDeleted=1 AND DeletedTime IS NOT NULL AND DeliveryStatus='DELETED'))
 );
 CREATE INDEX IX_InterviewAsset_Session ON dbo.InterviewAsset(SessionId,AssetType,CreateTime DESC);
 CREATE INDEX IX_InterviewAsset_Retention ON dbo.InterviewAsset(IsDeleted,RetainUntil,Id);
 CREATE INDEX IX_InterviewAsset_ProviderFileHash ON dbo.InterviewAsset(StorageProvider,ProviderFileHash) WHERE ProviderFileHash IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.InterviewTurn',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewTurn(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewTurn PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  QuestionId BIGINT NOT NULL,
  ParentTurnId BIGINT NULL,
  SequenceNo INT NOT NULL,
  AttemptNo INT NOT NULL CONSTRAINT DF_InterviewTurn_Attempt DEFAULT(1),
  PromptText NVARCHAR(2000) NOT NULL,
  Status VARCHAR(30) NOT NULL CONSTRAINT DF_InterviewTurn_Status DEFAULT('CREATED'),
  AudioAssetId BIGINT NULL,
  Transcript NVARCHAR(MAX) NULL,
  TranscriptJson NVARCHAR(MAX) NULL,
  TranscriptSource VARCHAR(20) NULL,
  TranscriptIsFinal BIT NOT NULL CONSTRAINT DF_InterviewTurn_TranscriptFinal DEFAULT(0),
  IsFinalAttempt BIT NOT NULL CONSTRAINT DF_InterviewTurn_FinalAttempt DEFAULT(0),
  ProviderRoundId VARCHAR(200) NULL,
  ProviderStartMs BIGINT NULL,
  ProviderEndMs BIGINT NULL,
  InterruptionCount INT NOT NULL CONSTRAINT DF_InterviewTurn_Interruptions DEFAULT(0),
  AsrLatencyMs INT NULL,
  LlmFirstTokenMs INT NULL,
  TtsFirstFrameMs INT NULL,
  StartedTime DATETIME2(3) NULL,
  EndedTime DATETIME2(3) NULL,
  DurationMs INT NULL,
  ErrorCode VARCHAR(100) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewTurn_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewTurn_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewTurn_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT FK_InterviewTurn_Question FOREIGN KEY(QuestionId) REFERENCES dbo.InterviewSessionQuestion(Id),
  CONSTRAINT FK_InterviewTurn_Parent FOREIGN KEY(ParentTurnId) REFERENCES dbo.InterviewTurn(Id),
  CONSTRAINT FK_InterviewTurn_AudioAsset FOREIGN KEY(AudioAssetId) REFERENCES dbo.InterviewAsset(Id),
  CONSTRAINT UQ_InterviewTurn_Session_Sequence_Attempt UNIQUE(SessionId,SequenceNo,AttemptNo),
  CONSTRAINT CK_InterviewTurn_Status CHECK(Status IN ('CREATED','PROMPTED','ANSWERING','FINAL_TRANSCRIPT_RECEIVED','COMPLETED','ASR_FAILED','RECORDING_EVIDENCE_MISSING','SKIPPED')),
  CONSTRAINT CK_InterviewTurn_Sequence CHECK(SequenceNo>0 AND AttemptNo>0),
  CONSTRAINT CK_InterviewTurn_Metrics CHECK(InterruptionCount>=0 AND (DurationMs IS NULL OR DurationMs>=0) AND (AsrLatencyMs IS NULL OR AsrLatencyMs>=0) AND (LlmFirstTokenMs IS NULL OR LlmFirstTokenMs>=0) AND (TtsFirstFrameMs IS NULL OR TtsFirstFrameMs>=0)),
  CONSTRAINT CK_InterviewTurn_ProviderTime CHECK((ProviderStartMs IS NULL AND ProviderEndMs IS NULL) OR (ProviderStartMs IS NOT NULL AND ProviderStartMs>=0 AND ProviderEndMs IS NOT NULL AND ProviderEndMs>=ProviderStartMs)),
  CONSTRAINT CK_InterviewTurn_Transcript CHECK((TranscriptIsFinal=0) OR (Transcript IS NOT NULL AND TranscriptSource IN ('ASR','MANUAL')))
 );
 CREATE INDEX IX_InterviewTurn_Session ON dbo.InterviewTurn(SessionId,SequenceNo,AttemptNo);
 CREATE INDEX IX_InterviewTurn_Question ON dbo.InterviewTurn(QuestionId,SessionId);
 CREATE INDEX IX_InterviewTurn_ProviderRound ON dbo.InterviewTurn(SessionId,ProviderRoundId) WHERE ProviderRoundId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.InterviewTranscriptCorrection',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewTranscriptCorrection(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewTranscriptCorrection PRIMARY KEY,
  TurnId BIGINT NOT NULL,
  PreviousTranscript NVARCHAR(MAX) NULL,
  CorrectedTranscript NVARCHAR(MAX) NOT NULL,
  Reason NVARCHAR(500) NOT NULL,
  CorrectedBy BIGINT NOT NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewTranscriptCorrection_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_InterviewTranscriptCorrection_Turn FOREIGN KEY(TurnId) REFERENCES dbo.InterviewTurn(Id),
  CONSTRAINT FK_InterviewTranscriptCorrection_User FOREIGN KEY(CorrectedBy) REFERENCES dbo.AppUser(Id),
  CONSTRAINT CK_InterviewTranscriptCorrection_Text CHECK(LEN(LTRIM(RTRIM(CorrectedTranscript)))>0 AND LEN(LTRIM(RTRIM(Reason))) BETWEEN 1 AND 500)
 );
 CREATE INDEX IX_InterviewTranscriptCorrection_Turn ON dbo.InterviewTranscriptCorrection(TurnId,CreateTime DESC);
END;

IF OBJECT_ID(N'dbo.InterviewRtcTask',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewRtcTask(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewRtcTask PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  Provider VARCHAR(30) NOT NULL CONSTRAINT DF_InterviewRtcTask_Provider DEFAULT('TENCENT_TRTC'),
  SdkAppId INT NOT NULL,
  RoomId VARCHAR(100) NOT NULL,
  RoomIdType VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewRtcTask_RoomType DEFAULT('STRING'),
  CandidateUserId VARCHAR(100) NOT NULL,
  RobotUserId VARCHAR(100) NOT NULL,
  ProviderTaskId VARCHAR(200) NULL,
  ProviderSessionId VARCHAR(200) NOT NULL,
  Status VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewRtcTask_Status DEFAULT('STARTING'),
  StartedTime DATETIME2(3) NULL,
  StoppedTime DATETIME2(3) NULL,
  StopDeadlineTime DATETIME2(3) NOT NULL,
  LastCallbackTime DATETIME2(3) NULL,
  ErrorCode VARCHAR(100) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewRtcTask_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewRtcTask_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewRtcTask_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT UQ_InterviewRtcTask_ProviderSession UNIQUE(Provider,ProviderSessionId),
  CONSTRAINT UQ_InterviewRtcTask_Room_Robot UNIQUE(SdkAppId,RoomId,RobotUserId),
  CONSTRAINT CK_InterviewRtcTask_Provider CHECK(Provider='TENCENT_TRTC'),
  CONSTRAINT CK_InterviewRtcTask_RoomType CHECK(RoomIdType IN ('STRING','NUMERIC')),
  CONSTRAINT CK_InterviewRtcTask_Status CHECK(Status IN ('STARTING','RUNNING','STOPPING','STOPPED','FAILED')),
  CONSTRAINT CK_InterviewRtcTask_Deadline CHECK(StopDeadlineTime>CreateTime),
  CONSTRAINT CK_InterviewRtcTask_Stop CHECK((Status<>'STOPPED' OR StoppedTime IS NOT NULL) AND (StoppedTime IS NULL OR StartedTime IS NULL OR StoppedTime>=StartedTime))
 );
 CREATE INDEX IX_InterviewRtcTask_Session ON dbo.InterviewRtcTask(SessionId,Status,CreateTime DESC);
 CREATE INDEX IX_InterviewRtcTask_Status_Deadline ON dbo.InterviewRtcTask(Status,StopDeadlineTime,Id);
 CREATE INDEX IX_InterviewRtcTask_ProviderTask ON dbo.InterviewRtcTask(Provider,ProviderTaskId) WHERE ProviderTaskId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.InterviewProviderEventInbox',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewProviderEventInbox(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewProviderEventInbox PRIMARY KEY,
  Provider VARCHAR(30) NOT NULL,
  DedupeKey CHAR(64) NOT NULL,
  EventType VARCHAR(50) NOT NULL,
  RtcTaskId BIGINT NULL,
  ProviderTaskId VARCHAR(200) NULL,
  ProviderRoundId VARCHAR(200) NULL,
  EventTime DATETIME2(3) NULL,
  PayloadHash BINARY(32) NOT NULL,
  PayloadJson NVARCHAR(MAX) NOT NULL,
  SignatureVersion VARCHAR(30) NULL,
  SignatureValid BIT NOT NULL,
  ProcessStatus VARCHAR(20) NOT NULL CONSTRAINT DF_InterviewProviderEventInbox_Status DEFAULT('RECEIVED'),
  RetryCount INT NOT NULL CONSTRAINT DF_InterviewProviderEventInbox_Retry DEFAULT(0),
  NextRetryTime DATETIME2(3) NULL,
  ReceiveTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewProviderEventInbox_Receive DEFAULT(SYSUTCDATETIME()),
  ProcessedTime DATETIME2(3) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewProviderEventInbox_RtcTask FOREIGN KEY(RtcTaskId) REFERENCES dbo.InterviewRtcTask(Id),
  CONSTRAINT UQ_InterviewProviderEventInbox_Dedupe UNIQUE(Provider,DedupeKey),
  CONSTRAINT CK_InterviewProviderEventInbox_Provider CHECK(Provider='TENCENT_TRTC'),
  CONSTRAINT CK_InterviewProviderEventInbox_Hash CHECK(DedupeKey NOT LIKE '%[^0-9A-Fa-f]%'),
  CONSTRAINT CK_InterviewProviderEventInbox_Status CHECK(ProcessStatus IN ('RECEIVED','PROCESSING','PROCESSED','RETRY','FAILED','IGNORED')),
  CONSTRAINT CK_InterviewProviderEventInbox_Retry CHECK(RetryCount>=0),
  CONSTRAINT CK_InterviewProviderEventInbox_Process CHECK((ProcessStatus<>'PROCESSED' OR ProcessedTime IS NOT NULL) AND (ProcessStatus<>'RETRY' OR NextRetryTime IS NOT NULL))
 );
 CREATE INDEX IX_InterviewProviderEventInbox_Process ON dbo.InterviewProviderEventInbox(ProcessStatus,NextRetryTime,ReceiveTime,Id);
 CREATE INDEX IX_InterviewProviderEventInbox_Task ON dbo.InterviewProviderEventInbox(RtcTaskId,ReceiveTime,Id) WHERE RtcTaskId IS NOT NULL;
 CREATE INDEX IX_InterviewProviderEventInbox_ProviderTask ON dbo.InterviewProviderEventInbox(ProviderTaskId,ReceiveTime,Id) WHERE ProviderTaskId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.InterviewReport',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewReport(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewReport PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  Status VARCHAR(30) NOT NULL,
  OverallScore DECIMAL(5,2) NULL,
  DimensionScoresJson NVARCHAR(MAX) NULL,
  EvidenceJson NVARCHAR(MAX) NULL,
  Summary NVARCHAR(MAX) NULL,
  Strengths NVARCHAR(MAX) NULL,
  Concerns NVARCHAR(MAX) NULL,
  Recommendation VARCHAR(30) NOT NULL,
  RiskFlagsJson NVARCHAR(MAX) NULL,
  RawModelJson NVARCHAR(MAX) NULL,
  ModelName VARCHAR(100) NULL,
  PromptVersion VARCHAR(50) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewReport_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewReport_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewReport_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT UQ_InterviewReport_Session UNIQUE(SessionId),
  CONSTRAINT CK_InterviewReport_Status CHECK(Status IN ('SUCCESS','REVIEW_REQUIRED','FAILED')),
  CONSTRAINT CK_InterviewReport_Score CHECK(OverallScore IS NULL OR OverallScore BETWEEN 0 AND 100),
  CONSTRAINT CK_InterviewReport_Recommendation CHECK(Recommendation IN ('PROCEED','HOLD','NOT_RECOMMENDED','INSUFFICIENT_DATA'))
 );
 CREATE INDEX IX_InterviewReport_Status ON dbo.InterviewReport(Status,UpdateTime DESC,Id);
END;

IF OBJECT_ID(N'dbo.InterviewReview',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewReview(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewReview PRIMARY KEY,
  SessionId BIGINT NOT NULL,
  ReviewerId BIGINT NOT NULL,
  Decision VARCHAR(30) NOT NULL,
  Reason NVARCHAR(1000) NOT NULL,
  ScoreOverride DECIMAL(5,2) NULL,
  BeforeJson NVARCHAR(MAX) NULL,
  AfterJson NVARCHAR(MAX) NULL,
  ReviewTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewReview_Time DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewReview_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT FK_InterviewReview_User FOREIGN KEY(ReviewerId) REFERENCES dbo.AppUser(Id),
  CONSTRAINT CK_InterviewReview_Decision CHECK(Decision IN ('PROCEED','HOLD','REJECT','REINTERVIEW','MANUAL_REQUIRED')),
  CONSTRAINT CK_InterviewReview_Reason CHECK(LEN(LTRIM(RTRIM(Reason))) BETWEEN 1 AND 1000),
  CONSTRAINT CK_InterviewReview_Score CHECK(ScoreOverride IS NULL OR ScoreOverride BETWEEN 0 AND 100)
 );
 CREATE INDEX IX_InterviewReview_Session ON dbo.InterviewReview(SessionId,ReviewTime DESC,Id);
 CREATE INDEX IX_InterviewReview_Reviewer ON dbo.InterviewReview(ReviewerId,ReviewTime DESC,Id);
END;

IF OBJECT_ID(N'dbo.InterviewAuditLog',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewAuditLog(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewAuditLog PRIMARY KEY,
  CorrelationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_InterviewAuditLog_Correlation DEFAULT(NEWID()),
  ActorType VARCHAR(20) NOT NULL,
  ActorUserId BIGINT NULL,
  ActorHash BINARY(32) NULL,
  ActionCode VARCHAR(80) NOT NULL,
  EntityType VARCHAR(50) NOT NULL,
  EntityId BIGINT NULL,
  SessionId BIGINT NULL,
  InvitationId BIGINT NULL,
  ResultCode VARCHAR(30) NOT NULL,
  BeforeJson NVARCHAR(MAX) NULL,
  AfterJson NVARCHAR(MAX) NULL,
  DetailJson NVARCHAR(MAX) NULL,
  IpHash BINARY(32) NULL,
  DeviceHash BINARY(32) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewAuditLog_Create DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_InterviewAuditLog_User FOREIGN KEY(ActorUserId) REFERENCES dbo.AppUser(Id),
  CONSTRAINT FK_InterviewAuditLog_Session FOREIGN KEY(SessionId) REFERENCES dbo.InterviewSession(Id),
  CONSTRAINT FK_InterviewAuditLog_Invitation FOREIGN KEY(InvitationId) REFERENCES dbo.InterviewInvitation(Id),
  CONSTRAINT CK_InterviewAuditLog_Actor CHECK(ActorType IN ('HR','CANDIDATE','SYSTEM','PROVIDER')),
  CONSTRAINT CK_InterviewAuditLog_Result CHECK(ResultCode IN ('SUCCESS','DENIED','FAILED','LOCKED','IGNORED')),
  CONSTRAINT CK_InterviewAuditLog_ActorIdentity CHECK(ActorUserId IS NOT NULL OR ActorHash IS NOT NULL OR ActorType IN ('SYSTEM','PROVIDER'))
 );
 CREATE INDEX IX_InterviewAuditLog_Session ON dbo.InterviewAuditLog(SessionId,CreateTime DESC,Id) WHERE SessionId IS NOT NULL;
 CREATE INDEX IX_InterviewAuditLog_Invitation ON dbo.InterviewAuditLog(InvitationId,CreateTime DESC,Id) WHERE InvitationId IS NOT NULL;
 CREATE INDEX IX_InterviewAuditLog_Action ON dbo.InterviewAuditLog(ActionCode,CreateTime DESC,Id);
 CREATE INDEX IX_InterviewAuditLog_Correlation ON dbo.InterviewAuditLog(CorrelationId,Id);
END;

/* Extend the existing leased AI task queue; realtime STT/TTS never enters AITask. */
IF COL_LENGTH(N'dbo.AITask',N'InterviewQuestionSetId') IS NULL
 ALTER TABLE dbo.AITask ADD InterviewQuestionSetId BIGINT NULL;
IF COL_LENGTH(N'dbo.AITask',N'InterviewSessionId') IS NULL
 ALTER TABLE dbo.AITask ADD InterviewSessionId BIGINT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AITask_InterviewQuestionSet')
 EXEC sp_executesql N'ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT FK_AITask_InterviewQuestionSet
  FOREIGN KEY(InterviewQuestionSetId) REFERENCES dbo.InterviewQuestionSet(Id);';
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AITask_InterviewSession')
 EXEC sp_executesql N'ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT FK_AITask_InterviewSession
  FOREIGN KEY(InterviewSessionId) REFERENCES dbo.InterviewSession(Id);';

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_AITask_Target' AND parent_object_id=OBJECT_ID(N'dbo.AITask'))
 ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Target;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_AITask_Type' AND parent_object_id=OBJECT_ID(N'dbo.AITask'))
 ALTER TABLE dbo.AITask DROP CONSTRAINT CK_AITask_Type;

ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT CK_AITask_Type CHECK(TaskType IN
 ('RESUME_IMPORT','RESUME_PARSE','MATCH_SCORE','KNOWLEDGE_EMBED','INTERVIEW_QUESTION_SET','INTERVIEW_REPORT'));

EXEC sp_executesql N'ALTER TABLE dbo.AITask WITH CHECK ADD CONSTRAINT CK_AITask_Target CHECK(
 (TaskType=''RESUME_IMPORT'' AND ResumeImportFileId IS NOT NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
 OR (TaskType=''RESUME_PARSE'' AND ResumeImportFileId IS NULL AND ResumeId IS NOT NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
 OR (TaskType=''MATCH_SCORE'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NOT NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
 OR (TaskType=''KNOWLEDGE_EMBED'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NOT NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NULL)
 OR (TaskType=''INTERVIEW_QUESTION_SET'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NOT NULL AND InterviewSessionId IS NULL)
 OR (TaskType=''INTERVIEW_REPORT'' AND ResumeImportFileId IS NULL AND ResumeId IS NULL AND CandidateJobId IS NULL AND KnowledgeDocumentId IS NULL AND InterviewQuestionSetId IS NULL AND InterviewSessionId IS NOT NULL)
);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AITask') AND name=N'IX_AITask_InterviewQuestionSet')
 EXEC sp_executesql N'CREATE INDEX IX_AITask_InterviewQuestionSet ON dbo.AITask(InterviewQuestionSetId,Status,CreateTime DESC)
  WHERE InterviewQuestionSetId IS NOT NULL;';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AITask') AND name=N'IX_AITask_InterviewSession')
 EXEC sp_executesql N'CREATE INDEX IX_AITask_InterviewSession ON dbo.AITask(InterviewSessionId,Status,CreateTime DESC)
  WHERE InterviewSessionId IS NOT NULL;';

/* HR management projection; candidate contact data stays outside the view. */
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
WHERE c.IsDeleted=0 AND j.IsDeleted=0;';

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 DECLARE @ErrorMessage NVARCHAR(4000),@ErrorSeverity INT,@ErrorState INT;
 SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
 RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;
GO

/* Post-deployment verification: expected counts are returned to sqlcmd/operator. */
SELECT COUNT(*) AS InterviewTableCount
FROM sys.tables
WHERE name IN (
 'InterviewPlan','InterviewQuestion','InterviewQuestionSet','InterviewSessionQuestion','InterviewQuestionSource',
 'InterviewInvitation','InterviewWechatIdentity','InterviewAccessSession','InterviewSession','InterviewConsent',
 'InterviewAsset','InterviewTurn','InterviewTranscriptCorrection','InterviewRtcTask','InterviewProviderEventInbox',
 'InterviewReport','InterviewReview','InterviewAuditLog'
);

SELECT name AS MissingRequiredObject
FROM (SELECT 'dbo.vw_InterviewManagementList' AS name) x
WHERE OBJECT_ID(x.name,N'V') IS NULL;
GO
