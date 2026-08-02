using NetBox.Models;

namespace NetBox.Core.Abstractions;

public interface IAccountService
{
  Task<CreateAccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
  Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
  Task<LoginResponse?> RefreshSessionAsync(string currentToken, CancellationToken cancellationToken = default);
  Task<CombinedProfileDto?> GetCurrentProfileAsync(string token, CancellationToken cancellationToken = default);
  Task<CombinedProfileDto?> UpdateCurrentProfileCustomizationAsync(string token, UpdateProfileCustomizationRequest request, CancellationToken cancellationToken = default);
  Task<LogoutResponse> LogoutAsync(string token, CancellationToken cancellationToken = default);
}
