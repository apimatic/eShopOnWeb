using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio Programmable Messaging implementation over plain HTTP (form-encoded posts, JSON reads).
/// Auth is HTTP Basic with AccountSid:AuthToken. The auth token is never logged and phone
/// numbers are never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private string MessagesUri => $"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    private string MessageUri(string sid) => $"/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.MessagingServiceSid))
        {
            return SmsSendResult.Failed("Scheduling requires Twilio:MessagingServiceSid to be configured.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<SmsMessageInfo?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(MessageUri(providerMessageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<TwilioMessage>(JsonOptions, cancellationToken);
        return message?.ToInfo();
    }

    public async Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        // A freshly created scheduled message can briefly 404 on update (provider read-after-write
        // lag), so retry a few times before giving up.
        for (var attempt = 1; ; attempt++)
        {
            var response = await _httpClient.PostAsync(MessageUri(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound || attempt >= 4)
            {
                var error = await ReadErrorAsync(response, cancellationToken);
                _logger.LogWarning("Could not cancel scheduled message {MessageSid}: {Error}", providerMessageSid, error);
                return false;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    public async Task<bool> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Per Twilio docs: POST an empty Body to redact the text while keeping the message record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var response = await _httpClient.PostAsync(MessageUri(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning("Could not redact message {MessageSid}: {Error}", providerMessageSid, error);
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number only. Twilio's DateSent
        // filters are date-granular (and combining <= with >= on the same field returns
        // nothing), so bound with >= from-date and < the day after to-date, then trim to
        // the exact date-time range below.
        var query = $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
                    $"&DateSent%3E={from.UtcDateTime:yyyy-MM-dd}" +
                    $"&DateSent%3C{to.UtcDateTime.AddDays(1):yyyy-MM-dd}" +
                    "&PageSize=1000";

        var results = new List<SmsMessageInfo>();
        string? nextUri = MessagesUri + query;
        while (nextUri != null)
        {
            var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioMessagePage>(JsonOptions, cancellationToken);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(m => m.ToInfo()));
            }
            nextUri = page?.NextPageUri;
        }

        return results
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when == null || (when >= from && when <= to);
            })
            .ToList();
    }

    private async Task<SmsSendResult> PostMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync(MessagesUri, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            // Provider error text can quote the destination number; shoppers' numbers must
            // never end up in logs, so scrub it before the error leaves this class.
            if (form.TryGetValue("To", out var to))
            {
                error = error.Replace(to, "[number]");
            }
            return SmsSendResult.Failed(error);
        }

        var message = await response.Content.ReadFromJsonAsync<TwilioMessage>(JsonOptions, cancellationToken);
        if (message?.Sid == null)
        {
            return SmsSendResult.Failed("Provider response did not include a message SID.");
        }
        return SmsSendResult.Accepted(message.Sid, message.Status ?? "queued");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioError>(JsonOptions, cancellationToken);
            if (error?.Message != null)
            {
                return $"Twilio error {error.Code}: {error.Message}";
            }
        }
        catch (JsonException) { }
        return $"Twilio request failed with status {(int)response.StatusCode}.";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ReadErrorAsync(response, cancellationToken));
        }
    }

    private class TwilioMessage
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public string? Body { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }

        // Twilio returns error_code as a JSON number.
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public SmsMessageInfo ToInfo() => new()
        {
            ProviderMessageSid = Sid ?? string.Empty,
            Status = Status ?? string.Empty,
            To = To,
            From = From,
            Body = Body,
            DateSent = ParseTwilioDate(DateSent),
            DateCreated = ParseTwilioDate(DateCreated),
            ErrorCode = ErrorCode?.ToString(CultureInfo.InvariantCulture),
            ErrorMessage = ErrorMessage
        };

        // Twilio returns RFC 2822 dates, e.g. "Thu, 27 Aug 2026 08:00:00 +0000".
        private static DateTimeOffset? ParseTwilioDate(string? value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
    }

    private class TwilioMessagePage
    {
        public List<TwilioMessage>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private class TwilioError
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
    }
}
