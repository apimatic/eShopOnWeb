using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// <see cref="ISmsGateway"/> implemented against the Twilio messaging API (v2010 Message resource) exactly as
/// described by the OpenAPI spec: send / schedule via <c>POST .../Messages.json</c>, read state via
/// <c>GET .../Messages/{Sid}.json</c>, cancel and redact via <c>POST .../Messages/{Sid}.json</c>, and list via
/// <c>GET .../Messages.json</c>. Basic auth (AccountSid:AuthToken) and the messaging base URL are configured on
/// the injected <see cref="HttpClient"/>.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    private string MessagesPath => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        var message = await PostFormForMessageAsync(MessagesPath, form, cancellationToken);
        return new SmsSendResult(message.Sid!, message.Status);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt (per the spec).
        // A specific From cannot be combined with scheduling; the sender is chosen from the service's pool.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        var message = await PostFormForMessageAsync(MessagesPath, form, cancellationToken);
        return new SmsSendResult(message.Sid!, message.Status);
    }

    public async Task<SmsMessageState> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessagePath(providerMessageSid), cancellationToken);
        var message = await ReadMessageAsync(response, cancellationToken);
        return ToState(message);
    }

    public async Task<SmsMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var message = await PostFormForMessageAsync(MessagePath(providerMessageSid), form, cancellationToken);
        return ToState(message);
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redacting the text content is an update with an empty Body (per the spec's redactBody example).
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostFormForMessageAsync(MessagePath(providerMessageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for messages sent from the configured sending number over the range, rather
        // than filtering a wider answer afterwards. The DateSent inequality filters are day-granular per the
        // spec, so we bound the query by day and then refine to the exact [from, to] window client-side.
        var fromDay = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Key names contain '>' / '<' (DateSent> / DateSent<), which encode to %3E / %3C.
        var nextUri =
            $"{MessagesPath}?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDay)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toDay)}" +
            "&PageSize=1000";

        var results = new List<ProviderMessageRecord>();

        while (!string.IsNullOrEmpty(nextUri))
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadAsAsync<TwilioListMessagesDto>(response, cancellationToken);

            foreach (var m in page.Messages ?? new List<TwilioMessageDto>())
            {
                var dateSent = ParseTwilioDate(m.DateSent);

                // Refine to the exact requested window when the provider reports a send time.
                if (dateSent is { } sent && (sent < from || sent > to))
                    continue;

                results.Add(new ProviderMessageRecord(
                    m.Sid ?? string.Empty,
                    m.Status,
                    m.To,
                    m.From,
                    dateSent,
                    m.ErrorCode,
                    m.ErrorMessage));
            }

            nextUri = page.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageDto> PostFormForMessageAsync(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private static SmsMessageState ToState(TwilioMessageDto message) =>
        new(message.Sid!, message.Status, message.ErrorCode, message.ErrorMessage);

    private async Task<TwilioMessageDto> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = await ReadAsAsync<TwilioMessageDto>(response, cancellationToken);
        if (string.IsNullOrEmpty(message.Sid))
            throw new TwilioApiException(response.StatusCode, null, "Provider response did not include a message sid.");
        return message;
    }

    private static async Task<T> ReadAsAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorDto? error = null;
            try { error = JsonSerializer.Deserialize<TwilioErrorDto>(payload); }
            catch (JsonException) { /* non-JSON error body; fall through with nulls */ }
            throw new TwilioApiException(response.StatusCode, error?.Code, error?.Message);
        }

        var result = JsonSerializer.Deserialize<T>(payload);
        if (result is null)
            throw new TwilioApiException(response.StatusCode, null, "Provider response could not be parsed.");
        return result;
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
