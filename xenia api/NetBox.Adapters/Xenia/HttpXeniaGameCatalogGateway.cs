using System.Net.Http.Json;
using NetBox.Models;

namespace NetBox.Adapters.Xenia;

public sealed class HttpXeniaGameCatalogGateway : IXeniaGameCatalogGateway
{
  private readonly HttpClient httpClient;

  public HttpXeniaGameCatalogGateway(HttpClient httpClient)
  {
    this.httpClient = httpClient;
  }

  public async Task<IReadOnlyList<XeniaGameCatalogItemDto>> GetGamesAsync(CancellationToken cancellationToken = default)
  {
    var response = await httpClient.GetAsync("/api/games", cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var payload = await response.Content.ReadFromJsonAsync<IReadOnlyList<XeniaGameCatalogItemDto>>(cancellationToken: cancellationToken).ConfigureAwait(false);
    return payload ?? Array.Empty<XeniaGameCatalogItemDto>();
  }
}
