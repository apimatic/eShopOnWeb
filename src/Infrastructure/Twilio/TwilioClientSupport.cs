using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioClientSupport
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static void Configure(HttpClient client, string baseUrl, TwilioOptions options)
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(options.AccountSid) && !string.IsNullOrWhiteSpace(options.AuthToken))
        {
            var bytes = Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        int? code = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(JsonOptions, cancellationToken);
            code = error?.Code;
        }
        catch (JsonException)
        {
            // Error bodies are not uniform across all Twilio hosts. Do not expose or log them.
        }

        throw new ProviderRequestException(operation, code);
    }

    internal static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
