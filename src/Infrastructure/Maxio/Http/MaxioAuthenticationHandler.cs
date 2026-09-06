using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Applies the specification's <c>BasicAuth</c> security scheme: the user name is the Maxio API
/// key and the password is the literal <c>x</c>.
/// </summary>
/// <remarks>
/// The header is built per request from <see cref="IOptionsMonitor{TOptions}"/> so a rotated key
/// is picked up without a restart, and so the key is never captured into a long-lived client.
/// </remarks>
public class MaxioAuthenticationHandler : DelegatingHandler
{
    private const string BasicAuthPassword = "x";

    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{BasicAuthPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return base.SendAsync(request, cancellationToken);
    }
}
