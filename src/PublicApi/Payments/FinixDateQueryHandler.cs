using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Finix requires <c>created_at.gte</c>/<c>created_at.lte</c> in <c>yyyy-MM-ddTHH:mm:ss</c> form,
/// while the generated SDK serializes a <see cref="DateTimeOffset"/> with fractional seconds and a
/// trailing <c>Z</c>. Normalise the two date query parameters at the transport seam so every
/// date-filtered list call is accepted by Finix.
/// </summary>
public sealed class FinixDateQueryHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri &&
            (uri.Query.Contains("created_at.gte=", StringComparison.Ordinal) ||
             uri.Query.Contains("created_at.lte=", StringComparison.Ordinal)))
        {
            var newQuery = Regex.Replace(
                uri.Query,
                @"(created_at\.(?:gte|lte)=)([^&]*)",
                match =>
                {
                    var raw = Uri.UnescapeDataString(match.Groups[2].Value);
                    if (!DateTimeOffset.TryParse(
                            raw,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var value))
                    {
                        return match.Value;
                    }

                    var normalized = value.ToUniversalTime()
                        .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    return match.Groups[1].Value + Uri.EscapeDataString(normalized);
                });

            request.RequestUri = new UriBuilder(uri) { Query = newQuery.TrimStart('?') }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
