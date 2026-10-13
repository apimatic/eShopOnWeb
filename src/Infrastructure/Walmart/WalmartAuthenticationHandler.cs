using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Delegating handler that attaches <c>Authorization: Bearer &lt;token&gt;</c> (RFC 6750, per the
/// v4 contract) to every Walmart API call, sourcing the token from <see cref="WalmartTokenProvider"/>.
/// On a 401 it invalidates the cached token and retries once, covering the window where a token
/// lapses between mint and use.
/// </summary>
public class WalmartAuthenticationHandler : DelegatingHandler
{
    private readonly WalmartTokenProvider _tokenProvider;

    public WalmartAuthenticationHandler(WalmartTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer any request body up front so the request can be replayed on a 401 retry.
        var buffered = await CloneAsync(request, cancellationToken);

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // Token may have expired; force a fresh one and retry once with a fresh request instance.
        response.Dispose();
        _tokenProvider.Invalidate();
        token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        buffered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(buffered, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var newContent = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = newContent;
        }

        return clone;
    }
}
