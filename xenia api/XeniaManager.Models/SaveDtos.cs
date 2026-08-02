namespace XeniaManager.Models;

public sealed record SaveFileDto(
  string Id,
  string TitleId,
  string Name,
  string Path,
  long SizeBytes,
  DateTimeOffset LastWriteTimeUtc);

public sealed record SaveBackupRequest(string ProfileId, string DestinationPath, IReadOnlyList<string>? SaveIds);

public sealed record SaveRestoreRequest(string ProfileId, string SourcePath, bool OverwriteExisting);

public sealed record SaveImportRequest(string ProfileId, string ImportPath, bool OverwriteExisting);

public sealed record SaveExportRequest(string ProfileId, string ExportPath, IReadOnlyList<string>? SaveIds);

public sealed record SaveOperationResultDto(bool Success, string Message, IReadOnlyList<string> AffectedSaveIds);
