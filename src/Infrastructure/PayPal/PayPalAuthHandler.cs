using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Attaches a cached PayPal bearer token to every outgoing gateway request.</summary>
public class PayPalAuthHandler : DelegatingHandler
{
    private readonly PayPalTokenProvider _tokenProvider;

    public PayPalAuthHandler(PayPalTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Named-client keys.</summary>
public static class PayPalHttpClientNames
{
    public const string Auth = "PayPalAuth";
    public const string Api = "PayPalApi";
}
