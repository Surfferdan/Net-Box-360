namespace NetBox.Models;

public sealed record CreateAccountRequest(string Username, string Password, string DisplayName, string? Email = null);

public sealed record LoginRequest(string Username, string Password);

public sealed record CreateAccountResponse(bool Success, long UserId, AccountProfileDto Profile);

public sealed record LoginResponse(string Token, long UserId);

public sealed record LogoutResponse(bool Success);

public sealed record AccountProfileDto(string Username, string DisplayName);
