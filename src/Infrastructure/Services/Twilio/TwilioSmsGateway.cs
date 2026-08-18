using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio Programmable Messaging gateway over plain HTTPS. The <see cref="HttpClient"/> is configured
/// (base address = <c>Twilio:BaseUrl</c> or api.twilio.com, plus Basic auth) by the DI registration.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    private string AccountPath => $"2010-04-01/Accounts/{_settings.AccountSid}";

    public async Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            // Send from this application's own configured number so reconciliation-by-From lines up.
            ["From"] = _settings.FromNumber
        };

        using var doc = await PostFormAsync($"{AccountPath}/Messages.json", form, cancellationToken);
        var root = doc.RootElement;
        return new SmsSendResult(
            GetString(root, "sid")!,
            GetString(root, "status"),
            GetInt(root, "error_code"),
            GetString(root, "error_message"));
    }

    public async Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is only available through a Messaging Service; From cannot be used with it.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var doc = await PostFormAsync($"{AccountPath}/Messages.json", form, cancellationToken);
        var root = doc.RootElement;
        return new SmsSendResult(
            GetString(root, "sid")!,
            GetString(root, "status"),
            GetInt(root, "error_code"),
            GetString(root, "error_message"));
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var _ = await PostFormAsync($"{AccountPath}/Messages/{providerMessageSid}.json", form, cancellationToken);
    }

    public async Task<SmsMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"{AccountPath}/Messages/{providerMessageSid}.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var doc = await ReadJsonOrThrowAsync(response, cancellationToken);
        var root = doc.RootElement;
        return new SmsMessageState(
            GetString(root, "sid")!,
            GetString(root, "status"),
            GetInt(root, "error_code"),
            GetString(root, "error_message"),
            ParseDate(GetString(root, "date_sent")),
            GetString(root, "from"),
            GetString(root, "to"));
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message text at Twilio while retaining the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var _ = await PostFormAsync($"{AccountPath}/Messages/{providerMessageSid}.json", form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Twilio's DateSent filter is day-granular; widen the query by a day on each side so nothing
        // in-range is missed, then the caller refines to the exact [from, to] window. The From filter
        // is applied at the provider so only this application's own sending number is returned.
        var lower = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var upper = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={lower}" +
            $"&{Uri.EscapeDataString("DateSent<")}={upper}" +
            "&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        string? nextPath = $"{AccountPath}/Messages.json?{query}";

        while (nextPath is not null)
        {
            using var response = await _httpClient.GetAsync(nextPath, cancellationToken);
            using var doc = await ReadJsonOrThrowAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessageRecord(
                        GetString(m, "sid")!,
                        GetString(m, "status"),
                        GetString(m, "from"),
                        GetString(m, "to"),
                        ParseDate(GetString(m, "date_sent")),
                        GetInt(m, "error_code")));
                }
            }

            var next = GetString(root, "next_page_uri");
            nextPath = string.IsNullOrEmpty(next) ? null : next.TrimStart('/');
        }

        return results;
    }

    private async Task<JsonDocument> PostFormAsync(string path, IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        return await ReadJsonOrThrowAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = $"HTTP {(int)response.StatusCode}";
            try
            {
                using var err = JsonDocument.Parse(payload);
                var code = GetInt(err.RootElement, "code");
                var message = GetString(err.RootElement, "message");
                if (code is not null || message is not null)
                {
                    detail = $"Twilio error {code}: {LogSanitizer.RedactPhoneNumbers(message)}";
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall back to the status code only (no raw body, may contain PII).
            }

            throw new SmsGatewayException($"Twilio messaging request failed ({detail}).");
        }

        return JsonDocument.Parse(payload);
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
