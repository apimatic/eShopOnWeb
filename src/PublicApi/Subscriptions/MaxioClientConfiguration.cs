using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioClientConfiguration
{
    public static Uri ResolveBaseAddress(MaxioOptions options, IConfiguration configuration)
    {
        options.Validate();
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return new Uri(options.BaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);

        // The OpenAPI server template defines US as chargify.com and EU as ebilling.maxio.com.
        var environment = configuration["MAXIO_ENVIRONMENT"] ?? "US";
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{options.Subdomain}.ebilling.maxio.com"
            : $"{options.Subdomain}.chargify.com";
        return new Uri($"https://{host}/", UriKind.Absolute);
    }

    public static void Configure(HttpClient client, MaxioOptions options, IConfiguration configuration)
    {
        client.BaseAddress = ResolveBaseAddress(options, configuration);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
