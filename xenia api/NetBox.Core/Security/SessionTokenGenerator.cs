using System.Security.Cryptography;

namespace NetBox.Core.Security;

public sealed class SessionTokenGenerator : ISessionTokenGenerator
{
  public string CreateToken()
  {
    var bytes = RandomNumberGenerator.GetBytes(32);
    return Convert.ToBase64String(bytes)
      .Replace('+', '-')
      .Replace('/', '_')
      .TrimEnd('=');
  }
}
