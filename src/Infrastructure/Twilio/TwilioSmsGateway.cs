using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioSmsGateway : ISmsGateway
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com/2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        var value = options.Value;
        _settings = new TwilioSettings
        {
            AccountSid = value.AccountSid?.Trim() ?? string.Empty,
            AuthToken = value.AuthToken?.Trim() ?? string.Empty,
            FromNumber = value.FromNumber?.Trim() ?? string.Empty,
            MessagingServiceSid = value.MessagingServiceSid?.Trim() ?? string.Empty,
            BaseUrl = string.IsNullOrWhiteSpace(value.BaseUrl) ? null : value.BaseUrl.Trim()
        };
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageResourceUrl(string sid) =>
        $"{MessagingBaseUrl}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsMessageSnapshot> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = _settings.FromNumber
        };

        if (request.SendAt.HasValue)
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUrl)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        httpRequest.Headers.Authorization = CreateAuthHeader();

        return await SendAndReadMessageAsync(httpRequest, treatHttpErrorAsFailedSnapshot: true, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageResourceUrl(providerMessageSid));
        httpRequest.Headers.Authorization = CreateAuthHeader();
        return await SendAndReadMessageAsync(httpRequest, treatHttpErrorAsFailedSnapshot: false, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageResourceUrl(providerMessageSid))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Status"] = "canceled"
            })
        };
        httpRequest.Headers.Authorization = CreateAuthHeader();
        return await SendAndReadMessageAsync(httpRequest, treatHttpErrorAsFailedSnapshot: false, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageResourceUrl(providerMessageSid))
        {
            Content = new StringContent("Body=", Encoding.ASCII, "application/x-www-form-urlencoded")
        };
        httpRequest.Headers.Authorization = CreateAuthHeader();
        return await SendAndReadMessageAsync(httpRequest, treatHttpErrorAsFailedSnapshot: false, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var url =
            $"{MessagesCollectionUrl}?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent>={Uri.EscapeDataString(fromUtc)}" +
            $"&DateSent<={Uri.EscapeDataString(toUtc)}" +
            "&PageSize=1000";

        var results = new List<SmsMessageSnapshot>();
        var pages = 0;
        const int maxPages = 100;

        while (!string.IsNullOrEmpty(url) && pages < maxPages)
        {
            pages++;
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = CreateAuthHeader();

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Provider message list failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new HttpRequestException($"Messaging provider list failed with HTTP {(int)response.StatusCode}.");
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            url = string.IsNullOrEmpty(page?.NextPageUri)
                ? null
                : MessagesCollectionUrl + ExtractQuery(page!.NextPageUri!);
        }

        return results;
    }

    private async Task<SmsMessageSnapshot> SendAndReadMessageAsync(
        HttpRequestMessage httpRequest,
        bool treatHttpErrorAsFailedSnapshot,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        TwilioMessageResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<TwilioMessageResponse>(payload);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Messaging provider returned a payload that could not be parsed. HTTP {StatusCode}.", (int)response.StatusCode);
        }

        if (parsed is not null && !string.IsNullOrEmpty(parsed.Sid))
        {
            return ToSnapshot(parsed);
        }

        if (!response.IsSuccessStatusCode && treatHttpErrorAsFailedSnapshot)
        {
            var error = TryReadError(payload);
            return new SmsMessageSnapshot(
                null,
                "failed",
                null,
                null,
                _settings.FromNumber,
                error?.Code,
                error?.Message,
                DateTimeOffset.UtcNow,
                null);
        }

        throw new HttpRequestException($"Messaging provider call failed with HTTP {(int)response.StatusCode}.");
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private static string ExtractQuery(string nextPageUri)
    {
        var uri = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri("https://placeholder.invalid" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri));
        return string.IsNullOrEmpty(uri.Query) ? string.Empty : uri.Query;
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResponse message)
    {
        return new SmsMessageSnapshot(
            message.Sid,
            message.Status ?? "unknown",
            message.Body,
            message.To,
            message.From,
            ReadErrorCode(message.ErrorCode),
            message.ErrorMessage,
            ParseTwilioDate(message.DateCreated),
            ParseTwilioDate(message.DateSent));
    }

    private static int? ReadErrorCode(JsonElement? errorCode)
    {
        if (errorCode is null || errorCode.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (errorCode.Value.ValueKind == JsonValueKind.Number && errorCode.Value.TryGetInt32(out var numeric))
        {
            return numeric;
        }

        if (errorCode.Value.ValueKind == JsonValueKind.String &&
            int.TryParse(errorCode.Value.GetString(), out var fromString))
        {
            return fromString;
        }

        return null;
    }

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

    private static TwilioErrorResponse? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorResponse>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TwilioMessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("error_code")]
        public JsonElement? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResponse>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
