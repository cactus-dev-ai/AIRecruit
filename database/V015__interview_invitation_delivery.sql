/* V015 - Interview invitation email/SMS delivery audit. SQL Server 2008 R2 compatible. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.InterviewInvitation',N'U') IS NULL
BEGIN
 RAISERROR(N'V015 requires V013 InterviewInvitation.',16,1);
 ROLLBACK TRANSACTION;
 RETURN;
END;

IF OBJECT_ID(N'dbo.InterviewInvitationDelivery',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InterviewInvitationDelivery(
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InterviewInvitationDelivery PRIMARY KEY,
  InvitationId BIGINT NOT NULL,
  Channel VARCHAR(20) NOT NULL,
  DestinationHash BINARY(32) NOT NULL,
  MaskedDestination NVARCHAR(100) NOT NULL,
  Status VARCHAR(20) NOT NULL,
  AttemptCount INT NOT NULL CONSTRAINT DF_InterviewInvitationDelivery_Attempts DEFAULT(1),
  Provider VARCHAR(30) NOT NULL,
  ProviderMessageId NVARCHAR(200) NULL,
  ErrorCode VARCHAR(100) NULL,
  ErrorMessage NVARCHAR(1000) NULL,
  LastAttemptTime DATETIME2(3) NOT NULL,
  SentTime DATETIME2(3) NULL,
  CreateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewInvitationDelivery_Create DEFAULT(SYSUTCDATETIME()),
  UpdateTime DATETIME2(3) NOT NULL CONSTRAINT DF_InterviewInvitationDelivery_Update DEFAULT(SYSUTCDATETIME()),
  RowVersion ROWVERSION NOT NULL,
  CONSTRAINT FK_InterviewInvitationDelivery_Invitation FOREIGN KEY(InvitationId) REFERENCES dbo.InterviewInvitation(Id),
  CONSTRAINT UQ_InterviewInvitationDelivery_Invitation_Channel UNIQUE(InvitationId,Channel),
  CONSTRAINT CK_InterviewInvitationDelivery_Channel CHECK(Channel IN('EMAIL','SMS')),
  CONSTRAINT CK_InterviewInvitationDelivery_Status CHECK(Status IN('SENT','FAILED')),
  CONSTRAINT CK_InterviewInvitationDelivery_Attempts CHECK(AttemptCount>0),
  CONSTRAINT CK_InterviewInvitationDelivery_Sent CHECK((Status='SENT' AND SentTime IS NOT NULL AND ErrorCode IS NULL) OR (Status='FAILED' AND ErrorCode IS NOT NULL))
 );
 CREATE INDEX IX_InterviewInvitationDelivery_Status ON dbo.InterviewInvitationDelivery(Status,LastAttemptTime,Id);
END;

COMMIT TRANSACTION;

SELECT CASE WHEN OBJECT_ID(N'dbo.InterviewInvitationDelivery',N'U') IS NOT NULL THEN 1 ELSE 0 END InterviewInvitationDeliveryReady;
