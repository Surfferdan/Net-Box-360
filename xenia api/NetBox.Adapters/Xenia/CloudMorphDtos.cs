namespace NetBox.Adapters.Xenia;

public sealed record CloudMorphCreateStreamRequest(
	string SessionId,
	string GameId,
	string GameTitle,
	string? CaptureMode,
	string? TargetWindowTitle,
	string? AudioInputDevice);

public sealed record CloudMorphCreateStreamResponse(
	string StreamId,
	string StreamUrl,
	string ControllerStatus,
	string Status,
	string? CaptureMode,
	string? TargetWindowTitle);

public sealed record CloudMorphStreamStatusResponse(string StreamId, string Status, string? Error);
