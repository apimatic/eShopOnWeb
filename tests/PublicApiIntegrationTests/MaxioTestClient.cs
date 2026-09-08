using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests;

/// <summary>
/// Direct Maxio Advanced Billing access used to clean up records created by tests.
/// Only reachable when the MAXIO_* environment variables are present.
/// </summary>
internal static class MaxioTestClient
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("MAXIO_API_KEY");
    private static string? Subdomain => Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Subdomain);

    public static async Task PurgeSubscriptionAsync(long subscriptionId, long customerId)
    {
        if (!IsConfigured)
        {
            return;
        }

        var baseUrl = $"https://{Subdomain}.chargify.com";
        using var client = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await client.DeleteAsync($"{baseUrl}/subscriptions/{subscriptionId}.json?ack={customerId}&cascade[]=customer");
    }
}
