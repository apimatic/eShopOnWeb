using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Configures the typed <see cref="HttpClient"/> that <c>MaxioBillingClient</c> uses. This is the
/// single place the outbound base URL is resolved (§2.2/§2.3): an explicit <c>Maxio:BaseUrl</c> wins,
/// otherwise the host is derived from the subdomain + region. Authentication uses Maxio's HTTP Basic
/// scheme (<c>&lt;api_key&gt;:x</c>). Both hosts call this from their composition root so retargeting
/// prod / dev / mock never leaks beyond this class.
/// </summary>
public static class MaxioHttpClientConfigurator
{
    public static void Configure(HttpClient http, MaxioSettings settings)
    {
        var baseUrl = settings.ResolveBaseUrl();
        // Ensure a trailing slash so relative request URLs preserve any base path (verbatim override).
        http.BaseAddress = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/");

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Maxio Advanced Billing authenticates with HTTP Basic: api_key as username, literal "x" as password.
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
