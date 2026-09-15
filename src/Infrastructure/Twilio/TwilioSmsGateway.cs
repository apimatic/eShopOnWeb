using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// <see cref="ISmsGateway"/> implemented against the Twilio REST API, built to the OpenAPI contract in
/// <c>api-specs/</c>. The messaging API (send/fetch/cancel/redact/list) is served from
/// <c>https://api.twilio.com</c> unless overridden by <c>Twilio:BaseUrl</c>; the Lookup API is served from
/// its own host and is not governed by that override. Auth is HTTP Basic (AccountSid:AuthToken).
/// The auth token is never logged, and destination numbers are never written to logs.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 1000; // safety backstop against a pagination loop

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup is served from its own host, not the messaging BaseUrl override.
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, "lookup", cancellationToken);

        var lookup = await ReadJsonAsync<TwilioLookupResponse>(response, cancellationToken);
        if (lookup is null)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "The provider returned no lookup result." });
        }

        return new PhoneNumberLookupResult(
            lookup.Valid,
            lookup.PhoneNumber,
            lookup.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    public async Task<SentSmsMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, "send", cancellationToken);
    }

    public async Task<SentSmsMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a provider capability: it requires a Messaging Service plus ScheduleType=fixed and SendAt.
        // The provider holds the message until SendAt; this application does not.
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, "schedule", cancellationToken);
    }

    private async Task<SentSmsMessage> CreateMessageAsync(Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}{AccountPath()}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, operation, cancellationToken);

        var message = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        if (message?.Sid is null)
        {
            throw new SmsGatewayException("The provider accepted the request but returned no message identifier.");
        }

        return new SentSmsMessage(message.Sid, message.Status ?? string.Empty, message.ErrorCode, message.ErrorMessage);
    }

    public async Task<SmsMessageState> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}{AccountPath()}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, "fetch", cancellationToken);

        var message = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        if (message is null)
        {
            throw new SmsGatewayException("The provider returned no message when fetching status.");
        }

        return new SmsMessageState(message.Status ?? string.Empty, message.ErrorCode, message.ErrorMessage);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Update the message with Status=canceled to call off a not-yet-sent (scheduled) message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var url = $"{_messagingBaseUrl}{AccountPath()}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, "cancel", cancellationToken);
        _ = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redact the message body by updating it to an empty string; the record and its outcome remain.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var url = $"{_messagingBaseUrl}{AccountPath()}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, "redact", cancellationToken);
        _ = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for messages sent from our configured number within the range. Sender and date
        // filtering are done by the provider (the account carries other traffic too), not after the fact.
        // Param names DateSent> and DateSent< map to >= and <= (inclusive bounds).
        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(ToTwilioDate(from))}" +
            $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(ToTwilioDate(to))}" +
            "&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        string? nextUrl = $"{_messagingBaseUrl}{AccountPath()}/Messages.json?{query}";

        for (var page = 0; nextUrl is not null && page < MaxReconciliationPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var response = await SendAsync(request, "reconcile", cancellationToken);
            var list = await ReadJsonAsync<TwilioMessageListResponse>(response, cancellationToken);
            if (list is null) break;

            foreach (var message in list.Messages)
            {
                if (message.Sid is null) continue;
                results.Add(new ProviderMessageRecord(
                    message.Sid,
                    message.To,
                    message.From,
                    message.Status ?? string.Empty,
                    message.ErrorCode,
                    ParseDate(message.DateSent)));
            }

            // next_page_uri is a path (and query) relative to the messaging host.
            nextUrl = string.IsNullOrEmpty(list.NextPageUri) ? null : $"{_messagingBaseUrl}{list.NextPageUri}";
        }

        return results;
    }

    private string AccountPath() => $"/2010-04-01/Accounts/{_settings.AccountSid}";

    private static string ToTwilioDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Do not include the request (it can carry a destination number) or credentials in the message.
            throw new SmsGatewayException($"Could not reach the messaging provider for the '{operation}' operation.", null, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, operation, cancellationToken);
        }

        return response;
    }

    private async Task ThrowForErrorAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        int? providerCode = null;
        var providerMessage = response.ReasonPhrase ?? "error";

        try
        {
            var error = await ReadJsonAsync<TwilioErrorResponse>(response, cancellationToken);
            if (error is not null)
            {
                providerCode = error.Code;
                if (!string.IsNullOrEmpty(error.Message)) providerMessage = error.Message!;
            }
        }
        catch
        {
            // Body was not the standard error envelope; fall back to the reason phrase.
        }

        _logger.LogWarning($"Twilio '{operation}' call returned HTTP {statusCode} (provider code {providerCode?.ToString() ?? "n/a"}).");
        response.Dispose();
        throw new SmsGatewayException($"The messaging provider rejected the '{operation}' request (HTTP {statusCode}): {providerMessage}", providerCode);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }
}
