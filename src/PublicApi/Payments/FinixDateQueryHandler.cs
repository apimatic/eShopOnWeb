using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Finix requires its date filters in the form <c>yyyy-MM-ddTHH:mm:ss</c> with no offset, while the
/// generated SDK serializes a <see cref="DateTimeOffset"/> as ISO-8601 with an offset. This handler
/// rewrites only the date-range query values, leaving every other parameter untouched.
/// </summary>
public sealed class FinixDateQueryHandler : DelegatingHandler
{
    private static readonly HashSet<string> DateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at.gte",
        "created_at.lte",
        "updated_at.gte",
        "updated_at.lte",
        "ready_to_settle_at.gte",
        "ready_to_settle_at.lte"
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri;
        if (uri is not null && uri.Query.Length > 1)
        {
            var parts = uri.Query.TrimStart('?').Split('&');
            var changed = false;

            for (var i = 0; i < parts.Length; i++)
            {
                var separator = parts[i].IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(parts[i][..separator]);
                if (!DateKeys.Contains(key))
                {
                    continue;
                }

                var rawValue = Uri.UnescapeDataString(parts[i][(separator + 1)..]);
                if (!DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
                {
                    continue;
                }

                parts[i] = $"{Uri.EscapeDataString(key)}=" +
                           Uri.EscapeDataString(value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                changed = true;
            }

            if (changed)
            {
                request.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", parts));
            }
        }

        return base.SendAsync(request, ct);
    }
}
