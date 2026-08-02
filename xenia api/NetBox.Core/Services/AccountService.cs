using NetBox.Adapters.Xenia;
using NetBox.Core.Abstractions;
using NetBox.Core.Security;
using NetBox.Data;
using NetBox.Data.Repositories;
using NetBox.Models;

namespace NetBox.Core.Services;

public sealed class AccountService : IAccountService
{
  private readonly INetBoxRepository repository;
  private readonly IXeniaProfileGateway xeniaProfileGateway;
  private readonly IPasswordHasher passwordHasher;
  private readonly ISessionTokenGenerator tokenGenerator;
  private readonly NetBoxDatabaseOptions databaseOptions;

  public AccountService(
    INetBoxRepository repository,
    IXeniaProfileGateway xeniaProfileGateway,
    IPasswordHasher passwordHasher,
    ISessionTokenGenerator tokenGenerator,
    Microsoft.Extensions.Options.IOptions<NetBoxDatabaseOptions> databaseOptions)
  {
    this.repository = repository;
    this.xeniaProfileGateway = xeniaProfileGateway;
    this.passwordHasher = passwordHasher;
    this.tokenGenerator = tokenGenerator;
    this.databaseOptions = databaseOptions.Value;
  }

  public async Task<CreateAccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
  {
    ValidateUsername(request.Username);
    var displayName = NormalizeDisplayName(request.DisplayName, request.Username);

    var existing = await repository.GetUserByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
    if (existing is not null)
    {
      throw new InvalidOperationException("Username already exists.");
    }

    var hashedPassword = passwordHasher.Hash(request.Password);
    var xeniaProfile = await xeniaProfileGateway.CreateProfileAsync(displayName, cancellationToken).ConfigureAwait(false);
    var now = DateTimeOffset.UtcNow;
    long userId;

    try
    {
      userId = await repository.CreateUserAsync(request.Username, request.Email, hashedPassword, xeniaProfile.Id, now, cancellationToken).ConfigureAwait(false);
      _ = await repository.UpsertSettingsAsync(userId, avatar: null, theme: "Metro", controllerPreference: "Xbox", language: "en-US", cancellationToken).ConfigureAwait(false);
      await repository.UpsertProfileCustomizationAsync(userId, displayName, string.Empty, "classic", avatarDataUrl: null, cancellationToken).ConfigureAwait(false);
    }
    catch
    {
      throw;
    }

    return new CreateAccountResponse(true, userId, new AccountProfileDto(request.Username, displayName));
  }

  public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
  {
    var user = await repository.GetUserByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
    if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
    {
      return null;
    }

    var token = tokenGenerator.CreateToken();
    var now = DateTimeOffset.UtcNow;
    var expiresAt = now.Add(databaseOptions.SessionLifetime);
    _ = await repository.CreateSessionAsync(user.Id, token, now, expiresAt, cancellationToken).ConfigureAwait(false);
    await repository.UpdateLastLoginAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
    return new LoginResponse(token, user.Id);
  }

  public async Task<LoginResponse?> RefreshSessionAsync(string currentToken, CancellationToken cancellationToken = default)
  {
    var existing = await repository.GetSessionByTokenAsync(currentToken, cancellationToken).ConfigureAwait(false);
    if (existing is null || existing.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      return null;
    }

    var newToken = tokenGenerator.CreateToken();
    var now = DateTimeOffset.UtcNow;
    var expiresAt = now.Add(databaseOptions.SessionLifetime);
    var refreshed = await repository.RefreshSessionAsync(currentToken, newToken, now, expiresAt, cancellationToken).ConfigureAwait(false);
    if (refreshed is null)
    {
      return null;
    }

    return new LoginResponse(newToken, refreshed.UserId);
  }

  public async Task<CombinedProfileDto?> GetCurrentProfileAsync(string token, CancellationToken cancellationToken = default)
  {
    var resolved = await ResolveCurrentSessionProfileAsync(token, cancellationToken).ConfigureAwait(false);
    if (resolved is null)
    {
      return null;
    }

    var (user, xeniaProfile, settingsRecord, customization) = resolved.Value;
    var settings = new NetBoxUserProfileDto(user.Id, user.Username, user.Email, settingsRecord.Avatar, settingsRecord.Theme, settingsRecord.ControllerPreference, settingsRecord.Language);
    return BuildCombinedProfile(user, xeniaProfile, settings, customization);
  }

  public async Task<CombinedProfileDto?> UpdateCurrentProfileCustomizationAsync(string token, UpdateProfileCustomizationRequest request, CancellationToken cancellationToken = default)
  {
    var resolved = await ResolveCurrentSessionProfileAsync(token, cancellationToken).ConfigureAwait(false);
    if (resolved is null)
    {
      return null;
    }

    var (user, xeniaProfile, settingsRecord, _) = resolved.Value;

    var nextDisplayName = NormalizeDisplayName(request.DisplayName, user.Username);
    var nextMotto = (request.Motto ?? string.Empty).Trim();
    if (nextMotto.Length > 120)
    {
      throw new ArgumentException("Motto must be 120 characters or fewer.", nameof(request));
    }

    var nextCardStyle = NormalizeCardStyle(request.CardStyle);
    var nextAvatarDataUrl = string.IsNullOrWhiteSpace(request.AvatarDataUrl)
      ? null
      : request.AvatarDataUrl;

    await repository.UpsertProfileCustomizationAsync(
      user.Id,
      nextDisplayName,
      nextMotto,
      nextCardStyle,
      nextAvatarDataUrl,
      cancellationToken).ConfigureAwait(false);

    var customization = new ProfileCustomizationDto(nextDisplayName, nextMotto, nextCardStyle, nextAvatarDataUrl);
    var settings = new NetBoxUserProfileDto(user.Id, user.Username, user.Email, settingsRecord.Avatar, settingsRecord.Theme, settingsRecord.ControllerPreference, settingsRecord.Language);
    return BuildCombinedProfile(user, xeniaProfile, settings, customization);
  }

  private async Task<(UserRecordDto User, NetBoxXeniaProfileDto XeniaProfile, UserSettingsDto Settings, ProfileCustomizationDto Customization)?> ResolveCurrentSessionProfileAsync(
    string token,
    CancellationToken cancellationToken)
  {
    var session = await repository.GetSessionByTokenAsync(token, cancellationToken).ConfigureAwait(false);
    if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      return null;
    }

    var user = await repository.GetUserByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);
    if (user is null)
    {
      return null;
    }

    var settingsRecord = await repository.GetSettingsAsync(user.Id, cancellationToken).ConfigureAwait(false)
      ?? new UserSettingsDto(0, user.Id, null, "Metro", "Xbox", "en-US");

    var xeniaProfile = await xeniaProfileGateway.GetProfileAsync(user.XeniaProfileId, cancellationToken).ConfigureAwait(false);
    if (xeniaProfile is null)
    {
      var repaired = await xeniaProfileGateway.CreateProfileAsync(user.Username, cancellationToken).ConfigureAwait(false);
      await repository.UpdateXeniaProfileIdAsync(user.Id, repaired.Id, cancellationToken).ConfigureAwait(false);
      xeniaProfile = repaired;
    }

    var customization = await repository.GetProfileCustomizationAsync(user.Id, cancellationToken).ConfigureAwait(false)
      ?? new ProfileCustomizationDto(
        NormalizeDisplayName(xeniaProfile.Gamertag, user.Username),
        string.Empty,
        "classic",
        null);

    return (user, xeniaProfile, settingsRecord, customization);
  }

  private static CombinedProfileDto BuildCombinedProfile(UserRecordDto user, NetBoxXeniaProfileDto xeniaProfile, NetBoxUserProfileDto settings, ProfileCustomizationDto customization)
  {
    var displayName = NormalizeDisplayName(customization.DisplayName, user.Username);
    var avatar = customization.AvatarDataUrl ?? settings.Avatar;
    return new CombinedProfileDto(
      user.Username,
      displayName,
      avatar,
      customization.Motto,
      NormalizeCardStyle(customization.CardStyle),
      xeniaProfile.Gamerscore,
      xeniaProfile.RecentGames,
      xeniaProfile.Achievements,
      settings,
      customization with { DisplayName = displayName, CardStyle = NormalizeCardStyle(customization.CardStyle) });
  }

  public async Task<LogoutResponse> LogoutAsync(string token, CancellationToken cancellationToken = default)
  {
    await repository.RevokeSessionAsync(token, cancellationToken).ConfigureAwait(false);
    return new LogoutResponse(true);
  }

  private static void ValidateUsername(string username)
  {
    if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 24)
    {
      throw new ArgumentException("Username must be between 3 and 24 characters.", nameof(username));
    }

    foreach (var ch in username)
    {
      if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
      {
        throw new ArgumentException("Username can only contain letters, numbers, underscore, and dash.", nameof(username));
      }
    }
  }

  private static string NormalizeDisplayName(string? value, string fallback)
  {
    var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    if (normalized.Length > 24)
    {
      normalized = normalized[..24];
    }

    return normalized;
  }

  private static string NormalizeCardStyle(string? value)
  {
    var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
    return normalized is "classic" or "emerald" or "sunset" or "midnight"
      ? normalized
      : "classic";
  }
}
