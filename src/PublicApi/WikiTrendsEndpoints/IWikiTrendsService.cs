using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;

public interface IWikiTrendsService
{
    Task<WikiEditsResponse> WatchAsync(int seconds, CancellationToken ct);
}
