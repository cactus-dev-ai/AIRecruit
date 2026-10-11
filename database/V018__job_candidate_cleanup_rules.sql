IF COL_LENGTH('dbo.Job', 'MaxCandidateAge') IS NULL
BEGIN
    ALTER TABLE dbo.Job ADD MaxCandidateAge INT NULL;
END;
GO

IF COL_LENGTH('dbo.Job', 'MaxExpectedMonthlySalary') IS NULL
BEGIN
    ALTER TABLE dbo.Job ADD MaxExpectedMonthlySalary DECIMAL(12,2) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Job_MaxCandidateAge')
BEGIN
    ALTER TABLE dbo.Job ADD CONSTRAINT CK_Job_MaxCandidateAge
        CHECK (MaxCandidateAge IS NULL OR MaxCandidateAge BETWEEN 16 AND 80);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Job_MaxExpectedMonthlySalary')
BEGIN
    ALTER TABLE dbo.Job ADD CONSTRAINT CK_Job_MaxExpectedMonthlySalary
        CHECK (MaxExpectedMonthlySalary IS NULL OR (MaxExpectedMonthlySalary > 0 AND MaxExpectedMonthlySalary <= 1000000));
END;
GO
