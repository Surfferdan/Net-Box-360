namespace NetBox.Core.Security;

public interface ISessionTokenGenerator
{
  string CreateToken();
}
