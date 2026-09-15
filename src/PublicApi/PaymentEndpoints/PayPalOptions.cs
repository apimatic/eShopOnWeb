using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? new Uri("https://api-m.paypal.com/")
            : Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
                ? new Uri("https://api-m.sandbox.paypal.com/")
                : throw new InvalidOperationException("PayPal:Environment must be Sandbox or Live.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal credentials are not configured.");
        if (Currency.Length != 3) throw new InvalidOperationException("PayPal:Currency must be a three-letter currency code.");
        _ = ResolveBaseUri();
    }

    public static void MapEnvironmentVariables(ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{SectionName}:ClientId"] = System.Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID"),
            [$"{SectionName}:ClientSecret"] = System.Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET"),
            [$"{SectionName}:Environment"] = System.Environment.GetEnvironmentVariable("PAYPAL_ENVIRONMENT"),
            [$"{SectionName}:Currency"] = System.Environment.GetEnvironmentVariable("PAYPAL_CURRENCY")
        };
        foreach (var key in values.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList())
            values.Remove(key);
        configuration.AddInMemoryCollection(values);
    }
}
