using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// Applies the <c>BasicAuth</c> security scheme of the specification to every outbound request:
/// the user name is the Maxio API key and the password is the literal <c>x</c>.
/// <para>
/// The header is attached here rather than on the <see cref="HttpClient"/> so a rotated key takes
/// effect on the next request (options are read through <see cref="IOptionsMonitor{T}"/>) and so
/// the key is never captured in a long-lived client instance.
/// </para>
/// </summary>
public sealed class MaxioAuthenticationHandler : DelegatingHandler
{
    private const string BasicPassword = "x";

    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BillingConfigurationException(
                "Maxio:ApiKey is not configured. Set it in user-secrets or the environment before calling the billing API.");
        }

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{apiKey}:{BasicPassword}"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.ParseAdd("application/json");

        return base.SendAsync(request, cancellationToken);
    }
}
