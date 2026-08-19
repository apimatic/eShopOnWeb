using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioClientSetup
{
    public static void Configure(HttpClient httpClient, MaxioSettings settings, string? environment = null)
        => MaxioApiClient.Configure(httpClient, settings, environment);
}
