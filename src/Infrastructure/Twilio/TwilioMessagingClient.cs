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
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio Messaging API (the 2010-04-01 REST API), built directly to the
/// OpenAPI contract in <c>api-specs/twilio/twilio_api_v2010</c>. Sends, reads, cancels, redacts and
/// lists messages. Honors the <c>Twilio:BaseUrl</c> override for every messaging call. The auth token
/// and destination numbers are never logged.
/// </summary>
public class TwilioMessagingClient : ISmsSender
{
    private const string MessagesPathFormat = "/2010-04-01/Accounts/{0}/Messages.json";
    private const string MessagePathFormat = "/2010-04-01/Accounts/{0}/Messages/{1}.json";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, TwilioSettings settings,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.EffectiveMessagingBaseUrl);
        if (_settings.HasCredentials)
        {
            var raw = Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }
    }

    public async Task<SmsMessageState> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            // Scheduling is a Messaging-Service-only capability: ScheduleType=fixed + SendAt (ISO 8601).
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
                throw new InvalidOperationException(
                    "Twilio:MessagingServiceSid is required to schedule a message.");

            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid!));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }
        else if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            form.Add(new("From", _settings.FromNumber!));
        }
        else if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid!));
        }
        else
        {
            throw new InvalidOperationException(
                "Either Twilio:FromNumber or Twilio:MessagingServiceSid must be configured to send a message.");
        }

        var path = string.Format(CultureInfo.InvariantCulture, MessagesPathFormat, _settings.AccountSid);
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        var resource = await ReadResourceOrThrowAsync(response, cancellationToken);
        _logger.LogInformation($"Twilio message created: sid={resource.Sid}, status={resource.Status}.");
        return ToState(resource);
    }

    public async Task<SmsMessageState?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = string.Format(CultureInfo.InvariantCulture, MessagePathFormat, _settings.AccountSid, messageSid);
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var resource = await ReadResourceOrThrowAsync(response, cancellationToken);
        return ToState(resource);
    }

    public async Task<SmsMessageState> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = string.Format(CultureInfo.InvariantCulture, MessagePathFormat, _settings.AccountSid, messageSid);
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        var resource = await ReadResourceOrThrowAsync(response, cancellationToken);
        _logger.LogInformation($"Twilio scheduled message canceled: sid={resource.Sid}, status={resource.Status}.");
        return ToState(resource);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = string.Format(CultureInfo.InvariantCulture, MessagePathFormat, _settings.AccountSid, messageSid);
        // Redaction: update the resource with an empty Body so the text is no longer retrievable,
        // while the record of the send and its outcome survive.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        var resource = await ReadResourceOrThrowAsync(response, cancellationToken);
        _logger.LogInformation($"Twilio message body redacted: sid={resource.Sid}.");
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListMessagesAsync(string fromNumber, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Ask the provider for THIS sending number's messages in the range (From + DateSent bounds),
        // rather than filtering a wider answer afterwards. The literal query keys are DateSent> (on/
        // after) and DateSent< (on/before), which must be percent-encoded (%3E / %3C).
        var basePath = string.Format(CultureInfo.InvariantCulture, MessagesPathFormat, _settings.AccountSid);
        var query = new StringBuilder(basePath);
        query.Append("?PageSize=1000");
        query.Append("&From=").Append(Uri.EscapeDataString(fromNumber));
        query.Append("&DateSent%3E=").Append(Uri.EscapeDataString(FormatIso(from)));
        query.Append("&DateSent%3C=").Append(Uri.EscapeDataString(FormatIso(to)));

        var results = new List<SmsMessageState>();
        string? nextPath = query.ToString();
        var safety = 0;

        while (!string.IsNullOrEmpty(nextPath) && safety++ < 10_000)
        {
            using var response = await _httpClient.GetAsync(nextPath, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw BuildException(response.StatusCode, body);

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(body);
            if (page?.Messages != null)
            {
                foreach (var m in page.Messages)
                    results.Add(ToState(m));
            }

            // next_page_uri is a path (relative to the messaging host); follow it verbatim.
            nextPath = page?.NextPageUri;
        }

        return results;
    }

    private void EnsureConfigured()
    {
        if (!_settings.HasCredentials || string.IsNullOrWhiteSpace(_settings.AccountSid))
            throw new InvalidOperationException(
                "Twilio is not configured. Set Twilio:AccountSid and Twilio:AuthToken (via user-secrets).");
    }

    private async Task<TwilioMessageResource> ReadResourceOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw BuildException(response.StatusCode, body);

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(body);
        if (resource is null || string.IsNullOrEmpty(resource.Sid))
            throw new TwilioApiException(response.StatusCode, null, "Twilio returned an unrecognized message payload.", null);
        return resource;
    }

    private static TwilioApiException BuildException(HttpStatusCode status, string body)
    {
        TwilioErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<TwilioErrorResponse>(body); }
        catch (JsonException) { /* non-JSON error body */ }
        return new TwilioApiException(status, error?.Code, error?.Message, error?.MoreInfo);
    }

    private static SmsMessageState ToState(TwilioMessageResource r) => new()
    {
        Sid = r.Sid ?? string.Empty,
        Status = r.Status,
        ErrorCode = r.ErrorCode,
        ErrorMessage = r.ErrorMessage,
        From = r.From,
        To = r.To,
        DateSent = ParseDate(r.DateSent),
        DateCreated = ParseDate(r.DateCreated)
    };

    private static string FormatIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // Twilio dates are RFC-2822 (e.g. "Wed, 19 Jun 2019 22:04:00 +0000").
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }
}
