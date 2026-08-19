using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioAuthenticationHandler : DelegatingHandler
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
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
