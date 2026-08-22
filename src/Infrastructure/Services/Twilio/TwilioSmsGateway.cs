using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioSmsGateway : ISmsGateway
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Authorization = CreateBasicAuth(_settings.AccountSid, _settings.AuthToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<SmsSendResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", command.To),
            new("From", _settings.FromNumber),
            new("Body", command.Body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (command.SendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", command.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                _logger.LogWarning("A scheduled message was requested without Twilio:MessagingServiceSid; the provider requires a Messaging Service to schedule.");
            }
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessagesCollectionPath()), content, cancellationToken);
        var payload = await ReadMessageAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode || payload?.Sid == null)
        {
            _logger.LogWarning(
                "Create Message returned HTTP {Status} with provider code {Code}.",
                (int)response.StatusCode,
                payload?.ErrorCode?.ToString() ?? "none");
            return new SmsSendResult(false, null, payload?.ErrorCode ?? (int)response.StatusCode);
        }

        return new SmsSendResult(true, ToSnapshot(payload), payload.ErrorCode);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ResolveMessagingUri(MessageInstancePath(providerSid)), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch Message {SidPrefix} returned HTTP {Status}.", SidPrefix(providerSid), (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        return payload == null ? null : ToSnapshot(payload);
    }

    public async Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessageInstancePath(providerSid)), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cancel Message {SidPrefix} returned HTTP {Status}.", SidPrefix(providerSid), (int)response.StatusCode);
            return await FetchAsync(providerSid, cancellationToken);
        }

        var payload = await ReadMessageAsync(response, cancellationToken);
        return payload == null ? null : ToSnapshot(payload);
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        using var response = await _httpClient.PostAsync(ResolveMessagingUri(MessageInstancePath(providerSid)), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Redact Message {SidPrefix} returned HTTP {Status}.", SidPrefix(providerSid), (int)response.StatusCode);
            throw new InvalidOperationException($"The provider refused to redact message content (HTTP {(int)response.StatusCode}).");
        }

        var payload = await ReadMessageAsync(response, cancellationToken);
        return payload == null ? null : ToSnapshot(payload);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromDate = from.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDateExclusive = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
                   $"&DateSent%3E={Uri.EscapeDataString(fromDate)}" +
                   $"&DateSent%3C={Uri.EscapeDataString(toDateExclusive)}" +
                   "&PageSize=1000";

        string? next = path;
        while (!string.IsNullOrEmpty(next))
        {
            using var response = await _httpClient.GetAsync(ResolveMessagingUri(next!), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List Message returned HTTP {Status}.", (int)response.StatusCode);
                break;
            }

            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResource>(JsonOptions, cancellationToken);
            if (page?.Messages == null)
            {
                break;
            }

            results.AddRange(page.Messages.Select(ToSnapshot));
            next = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private string MessagesCollectionPath() =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstancePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri ResolveMessagingUri(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            return new Uri($"{MessagingBaseUrl()}{absolute.PathAndQuery}");
        }

        var path = pathOrUri.StartsWith('/') ? pathOrUri : "/" + pathOrUri;
        return new Uri($"{MessagingBaseUrl()}{path}");
    }

    private string MessagingBaseUrl()
    {
        var configured = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.Trim().TrimEnd('/');
        return configured;
    }

    private async Task<TwilioMessageResource?> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unable to parse Message response: {Message}", PhoneNumberLogSanitizer.Redact(ex.Message));
            return null;
        }
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource) =>
        new(
            resource.Sid ?? string.Empty,
            resource.Status ?? string.Empty,
            resource.ErrorCode,
            resource.Body,
            resource.From,
            resource.To,
            ParseTwilioDate(resource.DateCreated),
            ParseTwilioDate(resource.DateSent));

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", raw);
    }

    private static string SidPrefix(string sid) =>
        sid.Length <= 4 ? "SM" : sid[..4];

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class TwilioMessageListResource
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
