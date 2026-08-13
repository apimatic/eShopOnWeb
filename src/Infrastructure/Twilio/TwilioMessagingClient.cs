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
/// Hand-written client for the Twilio Programmable Messaging API (api.v2010), built to the OpenAPI
/// spec in <c>api-specs/twilio/twilio_api_v2010</c>. Sends, reads, schedules, cancels, redacts and
/// lists SMS messages. HTTP basic auth (AccountSid:AuthToken); the auth token is never logged, and
/// the URL-level HttpClient logging (which would carry destination numbers) is removed at registration.
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;
    private readonly string _accountPath;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;

        // Twilio:BaseUrl overrides the messaging-API base address verbatim when set; otherwise the
        // provider default is used.
        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');
        _accountPath = $"/2010-04-01/Accounts/{_settings.AccountSid}";

        _http.DefaultRequestHeaders.Authorization = BuildBasicAuth(_settings.AccountSid, _settings.AuthToken);
    }

    internal static AuthenticationHeaderValue BuildBasicAuth(string username, string password)
    {
        var raw = Encoding.UTF8.GetBytes($"{username}:{password}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public Task<SmsMessageState> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsMessageState> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling a future message requires a Messaging Service and ScheduleType=fixed per the spec.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsMessageState> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}{_accountPath}/Messages/{Uri.EscapeDataString(providerSid)}.json";
        using var response = await _http.GetAsync(url, cancellationToken);
        var dto = await ReadMessageOrThrowAsync(response, "fetch message", cancellationToken);
        return Map(dto);
    }

    public async Task<SmsMessageState> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}{_accountPath}/Messages/{Uri.EscapeDataString(providerSid)}.json";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var dto = await ReadMessageOrThrowAsync(response, "cancel scheduled message", cancellationToken);
        return Map(dto);
    }

    public async Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // Setting Body to an empty string redacts the message text at the provider (per the spec).
        var url = $"{_baseUrl}{_accountPath}/Messages/{Uri.EscapeDataString(providerSid)}.json";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.PostAsync(url, content, cancellationToken);
        await ReadMessageOrThrowAsync(response, "redact message body", cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageState>();

        // Ask the provider directly for messages from the configured sending number over the range,
        // rather than filtering a wider answer after the fact. Date filters are day-granular at the
        // provider, so we widen to whole days and refine precisely below.
        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=')
            .Append(from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=')
            .Append(to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append("&PageSize=1000");

        string? nextUrl = $"{_baseUrl}{_accountPath}/Messages.json{query}";
        while (nextUrl is not null)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFromResponseAsync(response, "list messages", cancellationToken);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, JsonOptions);
            if (page is null)
            {
                break;
            }

            foreach (var message in page.Messages)
            {
                var state = Map(message);
                // Refine to the exact requested window (the provider filter was day-granular).
                if (state.SentAt is { } sentAt && (sentAt < from || sentAt > to))
                {
                    continue;
                }
                results.Add(state);
            }

            nextUrl = string.IsNullOrEmpty(page.NextPageUri) ? null : $"{_baseUrl}{page.NextPageUri}";
        }

        return results;
    }

    private async Task<SmsMessageState> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}{_accountPath}/Messages.json";
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var dto = await ReadMessageOrThrowAsync(response, "create message", cancellationToken);
        return Map(dto);
    }

    private async Task<TwilioMessageDto> ReadMessageOrThrowAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowFromResponseAsync(response, operation, cancellationToken);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions);
        return dto ?? throw new SmsGatewayException($"Twilio returned an empty body when trying to {operation}.");
    }

    private static async Task ThrowFromResponseAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        // Build a diagnostic that never includes free-text that could echo a destination number:
        // only the HTTP status, the Twilio error code, and the generic more_info URL.
        int? code = null;
        string? moreInfo = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
            code = error?.Code;
            moreInfo = error?.MoreInfo;
        }
        catch
        {
            // ignore parse issues; fall back to status only
        }

        var detail = code.HasValue ? $", code {code.Value}" : string.Empty;
        var info = string.IsNullOrEmpty(moreInfo) ? string.Empty : $" ({moreInfo})";
        throw new SmsGatewayException(
            $"Twilio API call to {operation} failed with HTTP {(int)response.StatusCode}{detail}{info}.");
    }

    private static SmsMessageState Map(TwilioMessageDto dto) => new(
        Sid: dto.Sid,
        Status: dto.Status ?? "unknown",
        ErrorCode: dto.ErrorCode,
        ErrorMessage: dto.ErrorMessage,
        To: dto.To,
        From: dto.From,
        SentAt: ParseTwilioDate(dto.DateSent));

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // Twilio date fields are RFC-2822 strings, e.g. "Fri, 24 May 2019 17:18:28 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
