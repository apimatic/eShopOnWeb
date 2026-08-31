using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written Twilio messaging client built against the OpenAPI contract in
/// api-specs/twilio/twilio_api_v2010 (Messages resource). Auth is HTTP Basic
/// with AccountSid:AuthToken per the spec's accountSid_authToken scheme.
/// Never logs destination numbers or credentials.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    // The messaging API base address; Twilio:BaseUrl overrides it verbatim.
    private string MessagingBaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? DefaultMessagingBaseUrl
        : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        var message = await PostMessageAsync(MessagesUrl, form, cancellationToken);
        return new SmsSendResult { MessageSid = message.Sid!, Status = message.Status ?? "queued" };
    }

    public async Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Message scheduling is a Messaging Services capability (ScheduleType=fixed + SendAt).
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        TwilioMessageResource message;
        try
        {
            message = await PostMessageAsync(MessagesUrl, form, cancellationToken);
        }
        catch (SmsProviderException)
        {
            // The configured number may not be in the messaging service's sender
            // pool; retry letting the service pick the sender.
            form.Remove("From");
            message = await PostMessageAsync(MessagesUrl, form, cancellationToken);
        }
        return new SmsSendResult { MessageSid = message.Sid!, Status = message.Status ?? "scheduled" };
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        var message = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToProviderMessage(message);
    }

    public async Task<ProviderMessage?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with Status=canceled cancels a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var message = await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
        return ToProviderMessage(message);
    }

    public async Task<ProviderMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with an empty Body redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var message = await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
        return ToProviderMessage(message);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromShopNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only
        // (the account carries other traffic). DateSent filters are date-granular
        // per the spec, so the exact [from, to] window is applied after fetching.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>=")}={from.UtcDateTime:yyyy-MM-dd}",
            $"{Uri.EscapeDataString("DateSent<")}={to.UtcDateTime.Date.AddDays(1):yyyy-MM-dd}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessage>();
        string? nextUrl = $"{MessagesUrl}?{query}";
        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var page = await ReadJsonAsync<TwilioListMessageResponse>(response, cancellationToken);
            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToProviderMessage));
            }
            nextUrl = string.IsNullOrEmpty(page.NextPageUri) ? null : MessagingBaseUrl + page.NextPageUri;
        }

        return results
            .Where(m => m.DateSent.HasValue
                ? m.DateSent.Value >= from && m.DateSent.Value <= to
                : false)
            .ToList();
    }

    private async Task<TwilioMessageResource> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        return await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResource? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResource>(payload, JsonOptions);
            }
            catch (JsonException) { /* fall through to generic error below */ }

            _logger.LogWarning("Twilio API call failed with HTTP {StatusCode} (provider error {ErrorCode}).",
                (int)response.StatusCode, error?.Code);
            throw new SmsProviderException(
                $"Twilio API call failed with HTTP {(int)response.StatusCode}: {error?.Message ?? "unexpected response"}",
                error?.Code);
        }

        var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (result == null)
        {
            throw new SmsProviderException("Twilio API returned an empty response body.");
        }
        return result;
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageResource message) => new()
    {
        MessageSid = message.Sid ?? string.Empty,
        Status = message.Status ?? string.Empty,
        ErrorCode = message.ErrorCode?.ToString(CultureInfo.InvariantCulture),
        ErrorMessage = message.ErrorMessage,
        DateSent = ParseTwilioDate(message.DateSent)
    };

    // Twilio renders dates as RFC-1123-ish strings, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParseExact(value, "ddd, dd MMM yyyy HH:mm:ss zzz",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
        {
            return exact;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }
}
