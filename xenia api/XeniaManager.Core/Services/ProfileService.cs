using XeniaManager.Core.Abstractions;
using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public sealed class ProfileService : IProfileService
{
  private readonly IXeniaProfileAdapter profileAdapter;
  private readonly IBackendEventSink eventSink;

  public ProfileService(IXeniaProfileAdapter profileAdapter, IBackendEventSink eventSink)
  {
    this.profileAdapter = profileAdapter;
    this.eventSink = eventSink;
  }

  public Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken = default)
    => profileAdapter.GetProfilesAsync(cancellationToken);

  public Task<ProfileDto?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
    => profileAdapter.GetProfileAsync(id, cancellationToken);

  public async Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
  {
    var profile = await profileAdapter.CreateProfileAsync(request, cancellationToken).ConfigureAwait(false);
    await eventSink.PublishAsync(new BackendEventDto(
      "ProfileChanged",
      DateTimeOffset.UtcNow,
      new Dictionary<string, string> { ["action"] = "created", ["profileId"] = profile.Id }), cancellationToken).ConfigureAwait(false);
    return profile;
  }

  public async Task<ProfileDto?> UpdateProfileAsync(string id, UpdateProfileRequest request, CancellationToken cancellationToken = default)
  {
    ProfileDto? updated = null;
    if (!string.IsNullOrWhiteSpace(request.Gamertag))
    {
      updated = await profileAdapter.RenameProfileAsync(id, request.Gamertag!, cancellationToken).ConfigureAwait(false);
    }

    if (request.IsActive is true)
    {
      _ = await profileAdapter.SetActiveProfileAsync(id, cancellationToken).ConfigureAwait(false);
      updated ??= await profileAdapter.GetProfileAsync(id, cancellationToken).ConfigureAwait(false);
    }

    if (updated is not null)
    {
      await eventSink.PublishAsync(new BackendEventDto(
        "ProfileChanged",
        DateTimeOffset.UtcNow,
        new Dictionary<string, string> { ["action"] = "updated", ["profileId"] = updated.Id }), cancellationToken).ConfigureAwait(false);
    }

    return updated;
  }

  public async Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
  {
    var deleted = await profileAdapter.DeleteProfileAsync(id, cancellationToken).ConfigureAwait(false);
    if (deleted)
    {
      await eventSink.PublishAsync(new BackendEventDto(
        "ProfileChanged",
        DateTimeOffset.UtcNow,
        new Dictionary<string, string> { ["action"] = "deleted", ["profileId"] = id }), cancellationToken).ConfigureAwait(false);
    }

    return deleted;
  }
}
