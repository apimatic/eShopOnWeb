using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public static class PayPalServerClientFactory
{
    public static PayPalServerSdkClient Create(PayPalOptions options, HttpClient httpClient)
    {
        var sdkOptions = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            }
        };

        // Optional override: used verbatim for every call including the token request.
        string? baseUrl = options.BaseUrl;

        // The SDK models only the sandbox environment; a "live" environment is reached
        // by overriding the base URL of the only server the SDK defines.
        if (string.IsNullOrWhiteSpace(baseUrl) &&
            options.Environment?.Equals("live", StringComparison.OrdinalIgnoreCase) == true)
        {
            baseUrl = "https://api-m.paypal.com";
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            sdkOptions.Server.Default.Sandbox.BaseUrl = baseUrl;
        }

        return new PayPalServerSdkClient(httpClient, sdkOptions);
    }
}