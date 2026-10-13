using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Attaches <c>Authorization: Bearer &lt;token&gt;</c> (the v4 scheme the Authorization contract
/// mandates) to every outbound Walmart API call, sourcing the token from
/// <see cref="IWalmartTokenProvider"/>. On a 401 the cached token is invalidated and the request is
/// retried once with a freshly minted token.
/// </summary>
public class WalmartAuthenticationHandler : DelegatingHandler
{
    private readonly IWalmartTokenProvider _tokenProvider;

    public WalmartAuthenticationHandler(IWalmartTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer any request content up front so the request can be safely replayed on a 401 retry.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync();
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokenProvider.Invalidate();
            response.Dispose();

            using var retry = await CloneAsync(request);
            token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await base.SendAsync(retry, cancellationToken);
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        if (request.Content is not null)
        {
            var buffer = await request.Content.ReadAsByteArrayAsync();
            var content = new ByteArrayContent(buffer);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = content;
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
