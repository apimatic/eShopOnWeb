using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public interface IWikiEditsWatcher
{
    Task<WikiTrendsResponse> WatchAsync(int seconds, CancellationToken ct);
}
