using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WikimediaEventStreams.Models;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public interface IWikiRevisionStream
{
    Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct);
}
