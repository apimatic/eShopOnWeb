using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Applies HTTP Basic authentication as defined by the Maxio OpenAPI security scheme:
/// username = API key, password = <c>x</c>.
/// </summary>
internal sealed class MaxioAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return base.SendAsync(request, cancellationToken);
    }
}
