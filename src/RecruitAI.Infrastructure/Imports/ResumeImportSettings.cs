namespace RecruitAI.Infrastructure.Imports;

public sealed record ResumeImportSettings(
    string ResumeTempRoot,
    int MaxFilesPerScan,
    int MinimumStableSeconds,
    long HardMaxSourceFileBytes,
    long JsonMaxFileBytes,
    long ManualMaxFileBytes,
    int ManualMaxFilesPerUpload);
