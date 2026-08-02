using NetBox.Models;

namespace NetBox.Adapters.Xenia;

public interface IXeniaGameCatalogGateway
{
  Task<IReadOnlyList<XeniaGameCatalogItemDto>> GetGamesAsync(CancellationToken cancellationToken = default);
}
