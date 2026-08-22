using System;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

internal static class PayPalUrl
{
    public const string SandboxBase = "https://api-m.sandbox.paypal.com";
    public const string LiveBase = "https://api-m.paypal.com";

    public static string ResolveBase(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.Trim().TrimEnd('/');
        }

        if (string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return LiveBase;
        }

        return SandboxBase;
    }

    public static Uri Combine(string baseUrl, string relativePath)
    {
        var trimmedBase = baseUrl.TrimEnd('/') + "/";
        var trimmedRelative = relativePath.TrimStart('/');
        return new Uri(new Uri(trimmedBase), trimmedRelative);
    }
}
