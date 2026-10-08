using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WikimediaEventStreams;
using WikimediaEventStreams.Models;
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public class WikiRevisionStreamService : IWikiRevisionStream
{
    private readonly WikimediaEventStreamsClient _client;

    public WikiRevisionStreamService(WikimediaEventStreamsClient client)
    {
        _client = client;
    }

    public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
    {
        return _client.MediawikiRevisionCreateEvents(
            new MediawikiRevisionCreateEventsRequest(),
            cancellationToken: ct);
    }
}
