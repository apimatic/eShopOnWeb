using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using FinixApiReference.Core.Hooks;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// The generated SDK serializes DateTimeOffset query values with a fractional-second and offset
/// form. Finix's list endpoints accept ISO-8601 date-times in the form yyyy-MM-ddTHH:mm:ss, so this
/// hook normalizes the date-range filters on the wire. It touches only known date filter keys.
/// </summary>
public static class FinixRequestHooks
{
    private static readonly string[] DateKeys =
    [
        "created_at.gte",
        "created_at.lte",
        "updated_at.gte",
        "updated_at.lte",
        "ready_to_settle_at.gte",
        "ready_to_settle_at.lte"
    ];

    public static SdkHook NormalizeDateFilters() =>
        SdkHook.OnRequest((request, _) => NormalizeDateFilters(request));

    private static void NormalizeDateFilters(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null || string.IsNullOrEmpty(uri.Query))
        {
            return;
        }

        var parts = uri.Query.TrimStart('?').Split('&');
        var changed = false;

        for (var i = 0; i < parts.Length; i++)
        {
            var pair = parts[i].Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[0]);
            if (!DateKeys.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[1]);
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                continue;
            }

            var normalized = parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            parts[i] = $"{pair[0]}={Uri.EscapeDataString(normalized)}";
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", parts) };
        request.RequestUri = builder.Uri;
    }
}
