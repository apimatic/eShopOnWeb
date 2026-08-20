using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingHost = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string FromNumber => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt is DateTimeOffset sendAt)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesCollectionPath(), fields, isIdempotent: false, cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
        => SendFormAsync(HttpMethod.Get, MessageInstancePath(messageSid), fields: null, isIdempotent: true, cancellationToken);

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), fields, isIdempotent: true, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), fields, isIdempotent: true, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromDate = from.UtcDateTime.Date.AddDays(-1);
        var toDate = to.UtcDateTime.Date.AddDays(1);
        var query = new List<string>
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"DateSent%3E={fromDate:yyyy-MM-dd}",
            $"DateSent%3C={toDate:yyyy-MM-dd}",
            "PageSize=1000"
        };

        var pathAndQuery = $"{MessagesCollectionPath()}?{string.Join("&", query)}";
        var results = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(pathAndQuery))
        {
            var json = await SendRawAsync(HttpMethod.Get, pathAndQuery, fields: null, isIdempotent: true, cancellationToken);
            var page = JsonSerializer.Deserialize<MessageListDto>(json, JsonOptions)
                       ?? new MessageListDto();

            foreach (var item in page.Messages ?? new List<MessageDto>())
            {
                var mapped = Map(item);
                var timestamp = mapped.DateSent ?? mapped.DateCreated;
                if (timestamp is null || (timestamp >= from && timestamp <= to))
                {
                    results.Add(mapped);
                }
            }

            pathAndQuery = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : PathAndQueryFrom(page.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage> SendFormAsync(
        HttpMethod method,
        string pathAndQuery,
        IReadOnlyList<KeyValuePair<string, string>>? fields,
        bool isIdempotent,
        CancellationToken cancellationToken)
    {
        var json = await SendRawAsync(method, pathAndQuery, fields, isIdempotent, cancellationToken);
        var dto = JsonSerializer.Deserialize<MessageDto>(json, JsonOptions)
                  ?? throw new TwilioApiException(0, null, "Twilio returned an empty message body.");
        return Map(dto);
    }

    private async Task<string> SendRawAsync(
        HttpMethod method,
        string pathAndQuery,
        IReadOnlyList<KeyValuePair<string, string>>? fields,
        bool isIdempotent,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildMessagingUri(pathAndQuery);

            using var response = await TwilioHttpRetry.SendAsync(_httpClient, () =>
            {
                var request = new HttpRequestMessage(method, uri);
                ApplyBasicAuth(request);
                if (fields is not null)
                {
                    request.Content = new FormUrlEncodedContent(fields);
                }

                return request;
            }, isIdempotent, cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioApiException.FromResponse((int)response.StatusCode, payload);
            }

            return payload;
        }
        catch (TwilioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TwilioApiException(0, null, PhoneNumberRedactor.Redact(ex.Message));
        }
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private Uri BuildMessagingUri(string pathAndQuery)
    {
        var trimmed = PathAndQueryFrom(pathAndQuery);
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingHost
            : _settings.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{trimmed}");
    }

    private static string PathAndQueryFrom(string uriFromProvider)
    {
        if (Uri.TryCreate(uriFromProvider, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return uriFromProvider.TrimStart('/');
    }

    private static ProviderMessage Map(MessageDto dto)
    {
        return new ProviderMessage
        {
            Sid = dto.Sid,
            Status = dto.Status,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            ErrorCode = dto.ErrorCode,
            ErrorMessage = PhoneNumberRedactor.Redact(dto.ErrorMessage),
            DateSent = ParseRfc2822(dto.DateSent),
            DateCreated = ParseRfc2822(dto.DateCreated)
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
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

    private sealed class MessageListDto
    {
        public List<MessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }
}
