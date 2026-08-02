using Microsoft.AspNetCore.Mvc;
using NetBox.Data.Repositories;
using NetBox.Models;
using XeniaManager.Api.Adapters;
using XeniaManager.Core.Services;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/netbox/social")]
public sealed class SocialController : ControllerBase
{
  private static readonly string[] AvatarPool =
  {
    "/assets/Assets/Profile/FriendPool/20002.png",
    "/assets/Assets/Profile/FriendPool/20003.png",
    "/assets/Assets/Profile/FriendPool/20006.png",
  };

  [HttpGet("feed")]
  public async Task<ActionResult<SocialFeedDto>> GetFeed(
    [FromServices] INetBoxRepository repository,
    [FromServices] IProfileService profiles,
    CancellationToken cancellationToken)
  {
    var currentUser = await ResolveCurrentUserAsync(repository, Request.Headers.Authorization.ToString(), cancellationToken).ConfigureAwait(false);
    if (currentUser is null)
    {
      return Ok(new SocialFeedDto(
        Array.Empty<FriendDto>(),
        new[]
        {
          new ActivityItemDto("session", "Sign in to load your NetBox social profile."),
        }));
    }

    var users = await repository.ListUsersAsync(cancellationToken).ConfigureAwait(false);
    var usersById = users.ToDictionary(user => user.Id);
    var links = await repository.ListFriendLinksAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
    var friendIds = links
      .Select(link => link.UserAId == currentUser.Id ? link.UserBId : link.UserAId)
      .Distinct()
      .Where(id => usersById.ContainsKey(id))
      .ToList();

    var friends = new List<FriendDto>(friendIds.Count);
    for (var index = 0; index < friendIds.Count; index++)
    {
      var user = usersById[friendIds[index]];
      var linkedProfile = await profiles.GetProfileAsync(user.XeniaProfileId, cancellationToken).ConfigureAwait(false);
      var customization = await repository.GetProfileCustomizationAsync(user.Id, cancellationToken).ConfigureAwait(false);
      var lastLoginUtc = user.LastLogin?.UtcDateTime;
      var online = lastLoginUtc is not null && lastLoginUtc > DateTime.UtcNow.AddDays(-1);
      var recentGame = linkedProfile?.RecentGames.FirstOrDefault()?.Name;
      var displayName = string.IsNullOrWhiteSpace(customization?.DisplayName)
        ? linkedProfile?.Gamertag ?? user.Username
        : customization.DisplayName;

      var activeSession = await repository.GetActiveGameSessionForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
      var joinableSession = activeSession is not null && activeSession.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
        ? activeSession
        : null;

      friends.Add(new FriendDto(
        Id: user.Id.ToString(),
        Gamertag: displayName,
        Subtitle: online ? "Online" : "Offline",
        Status: joinableSession is not null
          ? $"Playing {joinableSession.GameTitle}"
          : !string.IsNullOrWhiteSpace(recentGame)
            ? $"Last played {recentGame}"
            : online ? "Browsing dashboard" : "No recent activity",
        AvatarPath: AvatarPool[index % AvatarPool.Length],
        ActiveSessionId: joinableSession?.SessionId,
        ActiveGameTitle: joinableSession?.GameTitle));
    }

    var onlineCount = friends.Count(f => string.Equals(f.Subtitle, "Online", StringComparison.OrdinalIgnoreCase));
    var activity = new List<ActivityItemDto>
    {
      new($"accounts-{users.Count}", $"{users.Count} NetBox account(s) linked."),
      new($"friends-{friends.Count}", $"{friends.Count} friend link(s) in your list."),
      new($"online-{onlineCount}", $"{onlineCount} friend(s) online."),
    };

    var currentCustomization = await repository.GetProfileCustomizationAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
    var currentDisplayName = string.IsNullOrWhiteSpace(currentCustomization?.DisplayName)
      ? currentUser.Username
      : currentCustomization.DisplayName;
    activity.Add(new("session", $"Signed in as {currentUser.Username}."));
    activity.Add(new("identity", $"Welcome, {currentDisplayName}."));

    return Ok(new SocialFeedDto(friends, activity));
  }

  [HttpPost("friends")]
  public async Task<IActionResult> AddFriend(
    [FromBody] AddFriendRequest? request,
    [FromServices] INetBoxRepository repository,
    CancellationToken cancellationToken = default)
  {
    var currentUser = await ResolveCurrentUserAsync(repository, Request.Headers.Authorization.ToString(), cancellationToken).ConfigureAwait(false);
    if (currentUser is null)
    {
      return Unauthorized(new { success = false, error = "Sign in required." });
    }

    if (request is null)
    {
      return BadRequest(new { success = false, error = "Friend request payload is required." });
    }

    UserRecordDto? friend = null;
    if (!string.IsNullOrWhiteSpace(request.FriendUserId))
    {
      if (!long.TryParse(request.FriendUserId, out var friendId))
      {
        return BadRequest(new { success = false, error = "Friend user id is invalid." });
      }

      friend = await repository.GetUserByIdAsync(friendId, cancellationToken).ConfigureAwait(false);
    }
    else if (!string.IsNullOrWhiteSpace(request.Username))
    {
      friend = await repository.GetUserByUsernameAsync(request.Username.Trim(), cancellationToken).ConfigureAwait(false);
    }

    if (friend is null)
    {
      return NotFound(new { success = false, error = "Friend account not found." });
    }

    if (friend.Id == currentUser.Id)
    {
      return BadRequest(new { success = false, error = "You cannot add yourself as a friend." });
    }

    await repository.AddFriendLinkAsync(currentUser.Id, friend.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    return Ok(new { success = true, friendUserId = friend.Id.ToString(), friendUsername = friend.Username });
  }

  [HttpDelete("friends/{friendUserId:long}")]
  public async Task<IActionResult> RemoveFriend(
    long friendUserId,
    [FromServices] INetBoxRepository repository,
    CancellationToken cancellationToken = default)
  {
    var currentUser = await ResolveCurrentUserAsync(repository, Request.Headers.Authorization.ToString(), cancellationToken).ConfigureAwait(false);
    if (currentUser is null)
    {
      return Unauthorized(new { success = false, error = "Sign in required." });
    }

    if (friendUserId == currentUser.Id)
    {
      return BadRequest(new { success = false, error = "Invalid friend id." });
    }

    await repository.RemoveFriendLinkAsync(currentUser.Id, friendUserId, cancellationToken).ConfigureAwait(false);
    return Ok(new { success = true });
  }

  [HttpGet("chat")]
  public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetChatMessages(
    [FromServices] INetBoxRepository repository,
    [FromServices] IProfileService profiles,
    [FromQuery] int limit = 50,
    CancellationToken cancellationToken = default)
  {
    var currentUser = await ResolveCurrentUserAsync(repository, Request.Headers.Authorization.ToString(), cancellationToken).ConfigureAwait(false);
    if (currentUser is null)
    {
      return Unauthorized(new { success = false, error = "Sign in required." });
    }

    var safeLimit = Math.Clamp(limit, 1, 200);
    var messages = await repository.ListChatMessagesAsync(currentUser.Id, safeLimit, cancellationToken).ConfigureAwait(false);
    var users = await repository.ListUsersAsync(cancellationToken).ConfigureAwait(false);
    var usersById = users.ToDictionary(user => user.Id);
    var displayNameCache = new Dictionary<long, string>();

    async Task<string> ResolveDisplayNameAsync(long userId)
    {
      if (displayNameCache.TryGetValue(userId, out var cached))
      {
        return cached;
      }

      if (!usersById.TryGetValue(userId, out var user))
      {
        var fallback = $"User {userId}";
        displayNameCache[userId] = fallback;
        return fallback;
      }

      var customization = await repository.GetProfileCustomizationAsync(user.Id, cancellationToken).ConfigureAwait(false);
      var profile = await profiles.GetProfileAsync(user.XeniaProfileId, cancellationToken).ConfigureAwait(false);
      var resolved = string.IsNullOrWhiteSpace(customization?.DisplayName)
        ? string.IsNullOrWhiteSpace(profile?.Gamertag) ? user.Username : profile.Gamertag
        : customization.DisplayName;
      displayNameCache[userId] = resolved;
      return resolved;
    }

    var payload = new List<ChatMessageDto>(messages.Count);
    foreach (var message in messages.OrderBy(item => item.CreatedAt))
    {
      var from = await ResolveDisplayNameAsync(message.SenderUserId).ConfigureAwait(false);
      string? to = null;
      if (message.RecipientUserId is long recipientUserId)
      {
        to = await ResolveDisplayNameAsync(recipientUserId).ConfigureAwait(false);
      }

      payload.Add(new ChatMessageDto(
        message.Id.ToString(),
        from,
        to,
        message.Message,
        message.CreatedAt,
        message.SenderUserId == currentUser.Id));
    }

    return Ok(payload);
  }

  [HttpPost("chat")]
  public async Task<ActionResult<ChatMessageDto>> SendChatMessage(
    [FromBody] SendChatRequest? request,
    [FromServices] INetBoxRepository repository,
    [FromServices] IProfileService profiles,
    CancellationToken cancellationToken = default)
  {
    var currentUser = await ResolveCurrentUserAsync(repository, Request.Headers.Authorization.ToString(), cancellationToken).ConfigureAwait(false);
    if (currentUser is null)
    {
      return Unauthorized(new { success = false, error = "Sign in required." });
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Message))
    {
      return BadRequest(new { success = false, error = "Message is required." });
    }

    var trimmed = request.Message.Trim();
    if (trimmed.Length > 300)
    {
      return BadRequest(new { success = false, error = "Message must be 300 characters or fewer." });
    }

    long? recipientUserId = null;
    string? recipientName = null;
    if (!string.IsNullOrWhiteSpace(request.RecipientUserId))
    {
      if (!long.TryParse(request.RecipientUserId, out var recipientId))
      {
        return BadRequest(new { success = false, error = "Recipient user id is invalid." });
      }

      var recipient = await repository.GetUserByIdAsync(recipientId, cancellationToken).ConfigureAwait(false);
      if (recipient is null)
      {
        return NotFound(new { success = false, error = "Recipient was not found." });
      }

      recipientUserId = recipient.Id;
      var allowed = await repository.AreFriendsAsync(currentUser.Id, recipient.Id, cancellationToken).ConfigureAwait(false);
      if (!allowed)
      {
        return StatusCode(StatusCodes.Status403Forbidden, new { success = false, error = "You can only send direct messages to friends." });
      }

      var recipientCustomization = await repository.GetProfileCustomizationAsync(recipient.Id, cancellationToken).ConfigureAwait(false);
      if (!string.IsNullOrWhiteSpace(recipientCustomization?.DisplayName))
      {
        recipientName = recipientCustomization.DisplayName;
      }
      else
      {
        var recipientProfile = await profiles.GetProfileAsync(recipient.XeniaProfileId, cancellationToken).ConfigureAwait(false);
        recipientName = string.IsNullOrWhiteSpace(recipientProfile?.Gamertag) ? recipient.Username : recipientProfile.Gamertag;
      }
    }

    var createdAt = DateTimeOffset.UtcNow;
    var id = await repository.AddChatMessageAsync(currentUser.Id, recipientUserId, trimmed, createdAt, cancellationToken).ConfigureAwait(false);

    var senderCustomization = await repository.GetProfileCustomizationAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
    string senderName;
    if (!string.IsNullOrWhiteSpace(senderCustomization?.DisplayName))
    {
      senderName = senderCustomization.DisplayName;
    }
    else
    {
      var senderProfile = await profiles.GetProfileAsync(currentUser.XeniaProfileId, cancellationToken).ConfigureAwait(false);
      senderName = string.IsNullOrWhiteSpace(senderProfile?.Gamertag) ? currentUser.Username : senderProfile.Gamertag;
    }

    return Ok(new ChatMessageDto(
      id.ToString(),
      senderName,
      recipientName,
      trimmed,
      createdAt,
      true));
  }

  private static async Task<NetBox.Models.UserRecordDto?> ResolveCurrentUserAsync(
    INetBoxRepository repository,
    string authorizationHeader,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(authorizationHeader);
    if (token is null)
    {
      return null;
    }

    var session = await repository.GetSessionByTokenAsync(token, cancellationToken).ConfigureAwait(false);
    if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      return null;
    }

    return await repository.GetUserByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);
  }

  private static string? ReadBearerToken(string authorizationHeader)
  {
    if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return authorizationHeader[7..].Trim();
  }

  public sealed record FriendDto(string Id, string Gamertag, string Subtitle, string Status, string AvatarPath, string? ActiveSessionId = null, string? ActiveGameTitle = null);

  public sealed record ActivityItemDto(string Id, string Text);

  public sealed record SocialFeedDto(IReadOnlyList<FriendDto> Friends, IReadOnlyList<ActivityItemDto> Activity);

  public sealed record ChatMessageDto(string Id, string FromGamertag, string? ToGamertag, string Message, DateTimeOffset SentAtUtc, bool IsMine);

  public sealed record SendChatRequest(string Message, string? RecipientUserId);

  public sealed record AddFriendRequest(string? FriendUserId, string? Username);
}