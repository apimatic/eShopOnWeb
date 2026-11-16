using System;
using System.Text.RegularExpressions;
using FinixApiReference.Core.Hooks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Finix rejects date filters that contain fractional seconds, while the SDK's
/// date-filter formatter always emits millisecond precision. This hook uses the
/// SDK's own request pipeline to normalize those query values to the format
/// Finix documents (for example 2018-01-26T11:21:23Z).
/// </summary>
internal static class FinixQueryHooks
{
    private static readonly Regex FractionalSeconds =
        new(@"([?&](?:created_at|updated_at|ready_to_settle_at)\.(?:gte|lte)=[^&?]*?)(?:\.\d+)(Z?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SdkHook NormalizeDatePrecision { get; } = SdkHook.OnRequest((request, _) =>
    {
        var uri = request.RequestUri;
        if (uri is null)
        {
            return;
        }

        var text = uri.ToString();
        var normalized = FractionalSeconds.Replace(text, "$1$2");
        if (!string.Equals(text, normalized, StringComparison.Ordinal))
        {
            request.RequestUri = new Uri(normalized);
        }
    });
}
