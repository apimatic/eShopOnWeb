using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>Attaches the OAuth2 bearer token to every outgoing PayPal API call.</summary>
internal sealed class PayPalAuthHandler : DelegatingHandler
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
