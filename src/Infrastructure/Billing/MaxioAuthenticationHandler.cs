using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Applies HTTP Basic authentication as defined by the Maxio OpenAPI security scheme:
/// username = API key, password = <c>x</c>.
/// </summary>
internal sealed class MaxioAuthenticationHandler : DelegatingHandler
{
    private readonly IOptions<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptions<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey) && request.Headers.Authorization is null)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
