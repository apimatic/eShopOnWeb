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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Speaks to the Twilio Messages API (v2010) — the only component that talks to the provider's
/// messaging endpoints. Built directly to the OpenAPI contract in api-specs/twilio: HTTP Basic
/// auth (AccountSid:AuthToken), form-urlencoded requests, snake_case JSON responses, and the
/// <c>/2010-04-01/Accounts/{AccountSid}/Messages...</c> resource paths.
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;

        // Twilio:BaseUrl, when set, is used verbatim as the base address for every messaging call.
        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl.Trim();

        // HTTP Basic auth per the spec's securityScheme (accountSid_authToken). The auth token
        // is never logged and never surfaced anywhere else.
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string SenderNumber => _settings.FromNumber;

    public async Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        var message = await PostMessageAsync(CollectionUrl(), form, cancellationToken);
        return ToSendResult(message);
    }

    public async Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        var message = await PostMessageAsync(CollectionUrl(), form, cancellationToken);
        return ToSendResult(message);
    }

    public async Task<SmsMessageState?> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(InstanceUrl(sid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfProviderError(response, cancellationToken);
        var message = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return message is null ? null : ToMessageState(message);
    }

    public async Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Cancel a not-yet-sent (scheduled) message by updating its status to canceled.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        await PostMessageAsync(InstanceUrl(sid), form, cancellationToken);
    }

    public async Task RedactBodyAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Redact the message text at the provider by updating the body to an empty string.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(InstanceUrl(sid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(
        string fromNumber, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageState>();

        // Ask the provider to filter by sender (From) and by the sent-date range. DateSent is a
        // GMT *date* filter whose bounds are anchored at midnight at the start of each date:
        // DateSent> (>=) fromDate includes all of fromDate, while DateSent< (<=) toDate stops at
        // the start of toDate and would drop messages sent during it. So the upper bound uses the
        // day after `to`, guaranteeing the whole [from, to] range is covered.
        var fromDate = fromUtc.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toUtc.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        AppendQuery(query, "From", fromNumber);
        AppendQuery(query, "DateSent>", fromDate); // spec: DateSent> means on/after (>=) the date
        AppendQuery(query, "DateSent<", toDate);   // spec: DateSent< means on/before (<=) the date
        AppendQuery(query, "PageSize", "1000");

        var nextUrl = $"{CollectionUrl()}?{query}";
        var origin = new Uri(_baseUrl).GetLeftPart(UriPartial.Authority);
        var safetyLimit = 10_000; // guard against an unexpected pagination loop

        while (!string.IsNullOrEmpty(nextUrl) && safetyLimit-- > 0)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await ThrowIfProviderError(response, cancellationToken);

            var page = await ReadJsonAsync<TwilioMessageListResponse>(response, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToMessageState(message));
                }
            }

            // next_page_uri is host-relative; combine it with the (possibly overridden) origin.
            nextUrl = string.IsNullOrEmpty(page?.NextPageUri) ? null : origin + page!.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageResource> PostMessageAsync(
        string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await ThrowIfProviderError(response, cancellationToken);

        var message = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        if (message is null)
        {
            throw new SmsGatewayException("The provider returned an empty message response.");
        }
        return message;
    }

    private static SmsSendResult ToSendResult(TwilioMessageResource message) =>
        new(message.Sid ?? string.Empty,
            message.Status ?? string.Empty,
            message.ErrorCode,
            message.ErrorMessage);

    private static SmsMessageState ToMessageState(TwilioMessageResource message) =>
        new(message.Sid ?? string.Empty,
            message.Status ?? string.Empty,
            message.To,
            message.From,
            message.Body,
            ParseTwilioDate(message.DateSent),
            message.ErrorCode,
            message.ErrorMessage);

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio dates are RFC-2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private string CollectionUrl() =>
        $"{_baseUrl.TrimEnd('/')}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string InstanceUrl(string sid) =>
        $"{_baseUrl.TrimEnd('/')}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private static void AppendQuery(StringBuilder builder, string key, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('&');
        }
        builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task ThrowIfProviderError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        string message = $"The messaging provider returned {(int)response.StatusCode}.";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var error = JsonSerializer.Deserialize<TwilioErrorResponse>(body, JsonOptions);
                if (error is not null)
                {
                    code = error.Code;
                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        message = error.Message!;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to the status-code message if the error body is not the expected shape.
        }

        throw new SmsGatewayException(message, code);
    }
}
