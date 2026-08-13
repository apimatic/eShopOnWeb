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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio implementation of <see cref="ISmsSender"/> over plain HTTP.
///
/// Hosts:
///  - Messaging (create / read / update / list messages) uses <c>Twilio:BaseUrl</c> when set, else
///    the Twilio default <c>https://api.twilio.com</c>. The override is applied to every messaging call.
///  - Number validation uses the Lookup host <c>https://lookups.twilio.com</c>, which the override
///    does not govern.
///
/// The auth token is used only to build the HTTP Basic credential; it is never logged. Destination
/// numbers are never logged either.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsSender> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsSender(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credential = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 is served from lookups.twilio.com and is not governed by Twilio:BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Lookup returns 404 for a number it cannot parse into a valid range at all.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationError = "Number is not a valid destination." };
        }

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            throw new TwilioApiException((int)response.StatusCode, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        string? validationError = null;
        if (!valid && root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    parts.Add(e.GetString()!);
                }
            }
            validationError = parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        _logger.LogInformation("Twilio lookup completed. Valid={Valid}", valid);
        return new PhoneNumberLookupResult
        {
            Valid = valid,
            CanonicalNumber = valid ? canonical : null,
            ValidationError = valid ? null : (validationError ?? "Number is not a valid destination.")
        };
    }

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        var result = await CreateMessageAsync(form, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Twilio message created. Sid={Sid} Status={Status}", result.Sid, result.Status);
        return result;
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed; a plain From number cannot
        // be used to schedule. SendAt must be between 15 minutes and 35 days in the future.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        var result = await CreateMessageAsync(form, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Twilio message scheduled. Sid={Sid} Status={Status}", result.Sid, result.Status);
        return result;
    }

    public async Task<SmsSendResult> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = MessagesResource(providerMessageSid);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            throw new TwilioApiException((int)response.StatusCode, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        return ToSendResult(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        await PostToMessageAsync(providerMessageSid, form, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Twilio scheduled message canceled. Sid={Sid}", providerMessageSid);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message text at the provider while leaving the record
        // (sid, status, timestamps) intact. This is distinct from deleting the resource.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostToMessageAsync(providerMessageSid, form, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Twilio message body redacted. Sid={Sid}", providerMessageSid);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Ask the provider for this number's messages directly (From filter is applied server-side),
        // bounded coarsely by date; the precise [from, to] window is applied to date_sent afterwards.
        var fromDate = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(fromDate);
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(toDate);
        query.Append("&PageSize=1000");

        var results = new List<ProviderMessageRecord>();
        string? nextUri = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json{query}";

        while (!string.IsNullOrEmpty(nextUri))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
            using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ParseError(payload);
                throw new TwilioApiException((int)response.StatusCode, code, message);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var dateSent = ParseTwilioDate(GetString(m, "date_sent"));
                    // Refine to the exact requested window; records not sent in-range are excluded.
                    if (dateSent.HasValue && (dateSent.Value < from || dateSent.Value > to))
                    {
                        continue;
                    }
                    if (!dateSent.HasValue)
                    {
                        continue;
                    }

                    results.Add(new ProviderMessageRecord
                    {
                        Sid = GetString(m, "sid") ?? string.Empty,
                        Status = GetString(m, "status") ?? SmsDeliveryStatus.Unknown,
                        From = GetString(m, "from"),
                        To = GetString(m, "to"),
                        DateSent = dateSent,
                        ErrorCode = GetInt(m, "error_code")
                    });
                }
            }

            var next = GetString(root, "next_page_uri");
            nextUri = string.IsNullOrEmpty(next) ? null : $"{_messagingBaseUrl}{next}";
        }

        _logger.LogInformation("Twilio reconciliation listing returned {Count} in-range records.", results.Count);
        return results;
    }

    // ----- helpers -------------------------------------------------------------------------------

    private string MessagesCollection() =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessagesResource(string sid) =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesCollection())
        {
            Content = new FormUrlEncodedContent(form)
        };
        // A create is not automatically retried: retrying a send could produce a duplicate message.
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            throw new TwilioApiException((int)response.StatusCode, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        return ToSendResult(doc.RootElement);
    }

    private async Task<SmsSendResult> PostToMessageAsync(string sid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesResource(sid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(payload);
            throw new TwilioApiException((int)response.StatusCode, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        return ToSendResult(doc.RootElement);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read-only requests only. Retries transient transport failures / 5xx with a short backoff.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    response.Dispose();
                    await DelayForAttemptAsync(attempt, cancellationToken).ConfigureAwait(false);
                    request = CloneRequest(request);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await DelayForAttemptAsync(attempt, cancellationToken).ConfigureAwait(false);
                request = CloneRequest(request);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await DelayForAttemptAsync(attempt, cancellationToken).ConfigureAwait(false);
                request = CloneRequest(request);
            }
        }
    }

    private static Task DelayForAttemptAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        // Read requests carry no body, so a shallow clone of method + URI is sufficient to retry.
        return new HttpRequestMessage(request.Method, request.RequestUri);
    }

    private static SmsSendResult ToSendResult(JsonElement message)
    {
        return new SmsSendResult
        {
            Sid = GetString(message, "sid") ?? string.Empty,
            Status = GetString(message, "status") ?? SmsDeliveryStatus.Unknown,
            DateSent = ParseTwilioDate(GetString(message, "date_sent")),
            ErrorCode = GetInt(message, "error_code"),
            ErrorMessage = GetString(message, "error_message")
        };
    }

    private static (int? code, string message) ParseError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var code = GetInt(root, "code");
            var message = GetString(root, "message") ?? "Twilio request failed.";
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, "Twilio request failed.");
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
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
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
