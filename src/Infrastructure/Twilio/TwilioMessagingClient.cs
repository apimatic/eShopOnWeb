using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingRoot;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _messagingRoot = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<ProviderMessage> SendAsync(OutgoingSms message, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = message.To,
            ["Body"] = message.Body,
            ["From"] = _settings.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        if (message.SendAt.HasValue)
        {
            form["ScheduleType"] = "fixed";
            form["SendAt"] = message.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUri())
        {
            Content = new FormUrlEncodedContent(form)
        };
        ApplyBasicAuth(request);

        using var response = await TwilioHttpRetry.SendAsync(_httpClient, request, retryOnServerError: false, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioErrorParser.ToException(response.StatusCode, payload, "Create message failed.");
        }

        return Map(JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions));
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, MessageResourceUri(messageSid));
        ApplyBasicAuth(request);

        using var response = await TwilioHttpRetry.SendAsync(_httpClient, request, retryOnServerError: true, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioErrorParser.ToException(response.StatusCode, payload, "Fetch message failed.");
        }

        return Map(JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions));
    }

    public async Task<ProviderMessage> UpdateAsync(string messageSid, SmsUpdate update, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>();
        if (update.RedactBody)
        {
            form["Body"] = string.Empty;
        }

        if (update.Cancel)
        {
            form["Status"] = "canceled";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, MessageResourceUri(messageSid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        ApplyBasicAuth(request);

        using var response = await TwilioHttpRetry.SendAsync(_httpClient, request, retryOnServerError: true, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioErrorParser.ToException(response.StatusCode, payload, "Update message failed.");
        }

        return Map(JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions));
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}",
            "PageSize=1000"
        };

        var results = new List<ProviderMessage>();
        Uri? next = MessagesCollectionUri("?" + string.Join("&", query));

        while (next is not null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, next);
            ApplyBasicAuth(request);

            using var response = await TwilioHttpRetry.SendAsync(_httpClient, request, retryOnServerError: true, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioErrorParser.ToException(response.StatusCode, payload, "List messages failed.");
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, JsonOptions) ?? new TwilioMessageListDto();
            if (page.Messages is not null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(Map(item));
                }
            }

            next = ResolveNextPage(page.NextPageUri);
        }

        return results;
    }

    private Uri MessagesCollectionUri(string? query = null)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
        return new Uri(_messagingRoot + path + query, UriKind.Absolute);
    }

    private Uri MessageResourceUri(string messageSid)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        return new Uri(_messagingRoot + path, UriKind.Absolute);
    }

    private Uri? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(_messagingRoot + absolute.PathAndQuery, UriKind.Absolute);
        }

        var relative = nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri;
        return new Uri(_messagingRoot + relative, UriKind.Absolute);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static ProviderMessage Map(TwilioMessageDto? dto)
    {
        if (dto is null)
        {
            return new ProviderMessage();
        }

        return new ProviderMessage
        {
            Sid = dto.Sid,
            Status = dto.Status,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            ErrorCode = dto.ErrorCode,
            DateCreated = TwilioDate.Parse(dto.DateCreated),
            DateSent = TwilioDate.Parse(dto.DateSent),
            DateUpdated = TwilioDate.Parse(dto.DateUpdated)
        };
    }

    private sealed class TwilioMessageListDto
    {
        public List<TwilioMessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateCreated { get; set; }
        public string? DateSent { get; set; }
        public string? DateUpdated { get; set; }
    }
}
