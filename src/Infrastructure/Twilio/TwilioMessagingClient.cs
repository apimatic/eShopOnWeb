using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.SendAt is DateTimeOffset sendAt)
        {
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }
        else
        {
            form.Add(new("From", _settings.FromNumber));
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(CreateRequestUri(MessagesCollectionPath()), content, cancellationToken);
        var snapshot = await ReadMessageOrThrowAsync(response, cancellationToken);
        return snapshot;
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(CreateRequestUri(MessageInstancePath(messageSid)), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Status", "canceled")
        });
        using var response = await _httpClient.PostAsync(CreateRequestUri(MessageInstancePath(messageSid)), content, cancellationToken);
        return await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Body", string.Empty)
        });
        using var response = await _httpClient.PostAsync(CreateRequestUri(MessageInstancePath(messageSid)), content, cancellationToken);
        return await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListFromNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var uri = BuildListUri(from, to);

        while (!string.IsNullOrEmpty(uri))
        {
            using var response = await _httpClient.GetAsync(CreateRequestUri(uri), cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<ListMessageResponse>(JsonOptions, cancellationToken)
                ?? new ListMessageResponse();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            uri = ResolveNextPage(page.NextPageUri);
        }

        return results;
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private string BuildListUri(DateTimeOffset from, DateTimeOffset to)
    {
        var fromValue = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var toValue = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var fromNumber = Uri.EscapeDataString(_settings.FromNumber);
        return $"{MessagesCollectionPath()}?From={fromNumber}&DateSent%3E={fromValue}&DateSent%3C={toValue}&PageSize=1000";
    }

    private string? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                var configuredBase = new Uri(EnsureTrailingSlash(_settings.BaseUrl));
                return new Uri(configuredBase, absolute.PathAndQuery).ToString();
            }

            return absolute.PathAndQuery;
        }

        return nextPageUri;
    }

    private async Task<SmsMessageSnapshot> ReadMessageOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await TryReadErrorAsync(response, cancellationToken);
            throw new InvalidOperationException(error ?? $"Twilio messaging request failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioMessageResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Twilio messaging request returned an empty body.");
        return ToSnapshot(payload);
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(JsonOptions, cancellationToken);
            if (error?.Code is int code)
            {
                return $"Twilio messaging request failed with code {code}.";
            }
        }
        catch (JsonException)
        {
            // Fall through to a status-only error so response bodies (which may contain destinations) are never surfaced.
        }

        return $"Twilio messaging request failed with status {(int)response.StatusCode}.";
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResponse message)
    {
        return new SmsMessageSnapshot
        {
            Sid = message.Sid,
            Status = message.Status,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            Body = message.Body,
            From = message.From,
            To = message.To,
            DateCreated = message.DateCreated,
            DateSent = message.DateSent,
            CreatedAt = ParseTwilioDate(message.DateCreated),
            SentAt = ParseTwilioDate(message.DateSent)
        };
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

    private string MessagingBaseUrl()
    {
        return string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl.TrimEnd('/');
    }

    internal Uri CreateRequestUri(string relativeOrAbsolute)
    {
        var baseUri = new Uri(EnsureTrailingSlash(MessagingBaseUrl()));
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return new Uri(baseUri, absolute.PathAndQuery.TrimStart('/'));
            }

            return absolute;
        }

        return new Uri(baseUri, relativeOrAbsolute.TrimStart('/'));
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private sealed class TwilioMessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? DateCreated { get; set; }
        public string? DateSent { get; set; }
    }

    private sealed class ListMessageResponse
    {
        public List<TwilioMessageResponse>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        public int? Code { get; set; }
        public int? Status { get; set; }
    }
}
