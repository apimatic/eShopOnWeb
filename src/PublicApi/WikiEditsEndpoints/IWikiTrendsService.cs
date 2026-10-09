using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public interface IWikiTrendsService
{
    Task<WikiEditsResponse> GetWikiEditsAsync(int seconds, CancellationToken ct);
}
