using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/>, built by hand against the Twilio OpenAPI
/// specification (api-specs/). Messaging goes through the 2010-04-01 Messages resource; number
/// validation goes through the Lookups v2 API. Auth is HTTP Basic (AccountSid:AuthToken) per the spec.
///
/// Privacy: recipient numbers and message bodies are PII and are never logged. Only SIDs, statuses,
/// HTTP status codes and the provider's own error codes/messages are logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsProvider> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;

        // Twilio:BaseUrl overrides the MESSAGING API base only; Lookups always uses its own host.
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        // A number the provider cannot resolve at all comes back 404 — treat as "not a usable destination".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneLookupResult(false, null, new[] { "NOT_FOUND" });
        }

        await EnsureSuccessAsync(response, "Lookup", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(body, JsonOptions)
            ?? throw new TwilioApiException((int)response.StatusCode, null, "Empty Lookups response.");

        IReadOnlyList<string> errors = lookup.ValidationErrors ?? new List<string>();
        return new PhoneLookupResult(lookup.Valid, lookup.PhoneNumber, errors);
    }

    public Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        // Immediate send from the account's configured sending number.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toPhoneNumber),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toPhoneNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{MessagesBase()}/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "FetchMessage", cancellationToken);
        var resource = await ReadMessageAsync(response, cancellationToken);
        return ToResult(resource);
    }

    public Task<SmsSendResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redact the text at the provider: POST the message with an empty Body.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        return UpdateMessageAsync(providerMessageSid, form, "RedactMessage", cancellationToken);
    }

    public Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Cancel a not-yet-sent scheduled message: POST the message with Status=canceled.
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        return UpdateMessageAsync(providerMessageSid, form, "CancelMessage", cancellationToken);
    }

    public async Task<IReadOnlyList<SmsSendResult>> ListOwnMessagesAsync(DateTimeOffset dateSentFrom, DateTimeOffset dateSentTo, CancellationToken cancellationToken = default)
    {
        // Ask the provider for messages sent from THIS application's own sending number, in range.
        // The From filter and the date bounds are applied by the provider (not post-filtered here).
        var from = Uri.EscapeDataString(_settings.FromNumber);
        var after = dateSentFrom.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var before = dateSentTo.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Note: query keys DateSent> / DateSent< are URL-encoded (%3E / %3C) per the spec's examples.
        var relative = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json"
            + $"?From={from}&DateSent%3E={after}&DateSent%3C={before}&PageSize=1000";

        var results = new List<SmsSendResult>();
        var pages = 0;
        string? next = relative;
        while (next is not null && pages < MaxReconciliationPages)
        {
            var url = next.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? next : _messagingBaseUrl + next;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "ListMessages", cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                ?? new TwilioMessageListResponse();

            foreach (var message in list.Messages)
            {
                results.Add(ToResult(message));
            }

            next = list.NextPageUri;
            pages++;
        }

        if (next is not null)
        {
            _logger.LogWarning("Reconciliation reached the {MaxPages}-page cap; results may be truncated.", MaxReconciliationPages);
        }

        return results;
    }

    private async Task<SmsSendResult> CreateMessageAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        var url = $"{MessagesBase()}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "CreateMessage", cancellationToken);
        var resource = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message created: sid={Sid} status={Status}.", resource.Sid, resource.Status);
        return ToResult(resource);
    }

    private async Task<SmsSendResult> UpdateMessageAsync(string sid, IEnumerable<KeyValuePair<string, string>> form, string operation, CancellationToken cancellationToken)
    {
        var url = $"{MessagesBase()}/{Uri.EscapeDataString(sid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, operation, cancellationToken);
        var resource = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio {Operation} succeeded: sid={Sid} status={Status}.", operation, resource.Sid, resource.Status);
        return ToResult(resource);
    }

    private string MessagesBase() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages";

    private static async Task<TwilioMessageResource> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions)
            ?? throw new TwilioApiException((int)response.StatusCode, null, "Empty message response from provider.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        int? providerCode = null;
        string providerMessage = response.ReasonPhrase ?? "request failed";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var error = JsonSerializer.Deserialize<TwilioErrorResponse>(body, JsonOptions);
                if (error is not null && error.Code != 0)
                {
                    providerCode = error.Code;
                    providerMessage = error.Message ?? providerMessage;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — keep the reason phrase; do not surface raw content.
        }

        _logger.LogWarning("Twilio {Operation} failed: http={HttpStatus} code={ProviderCode} message={ProviderMessage}.",
            operation, (int)response.StatusCode, providerCode, providerMessage);
        throw new TwilioApiException((int)response.StatusCode, providerCode, $"Twilio {operation} failed: {providerMessage}");
    }

    private static SmsSendResult ToResult(TwilioMessageResource resource) => new(
        resource.Sid,
        resource.Status ?? "unknown",
        resource.ErrorCode,
        resource.ErrorMessage,
        resource.From,
        ParseTwilioDate(resource.DateSent),
        ParseTwilioDate(resource.DateCreated));

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
