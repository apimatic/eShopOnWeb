using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Applies the Maxio credentials to every outbound call. Maxio uses HTTP Basic with the API key as
/// the username and the literal "x" as the password, so the key only ever leaves as an
/// Authorization header - it is never placed in a URL, a query string, or a log.
/// </summary>
public class MaxioAuthenticationHandler : DelegatingHandler
{
    private const string BasicScheme = "Basic";
    private const string ApiKeyPassword = "x";

    private readonly IOptionsMonitor<MaxioSettings> _settings;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioSettings> settings)
    {
        _settings = settings;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _settings.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BillingConfigurationException(
                $"'{MaxioSettings.CONFIG_SECTION}:ApiKey' is not configured; the billing provider cannot be called.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(BasicScheme, EncodeCredentials(apiKey));

        return base.SendAsync(request, cancellationToken);
    }

    private static string EncodeCredentials(string apiKey)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{ApiKeyPassword}"));
}
