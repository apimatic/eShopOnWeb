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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessageClient : ITwilioMessageClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const int PageSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessageClient> _logger;

    public TwilioMessageClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessageClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioMessageSnapshot> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields.Add(new("From", _settings.FromNumber));
        }

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (sendAt is not null)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUri())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        AddBasicAuth(request);

        var resource = await SendAndReadAsync(request, cancellationToken);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstanceUri(messageSid));
        AddBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = await ReadResourceAsync(response, cancellationToken);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new[] { new KeyValuePair<string, string>("Status", "canceled") };
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageInstanceUri(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        AddBasicAuth(request);

        var resource = await SendAndReadAsync(request, cancellationToken);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new[] { new KeyValuePair<string, string>("Body", string.Empty) };
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageInstanceUri(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        AddBasicAuth(request);

        var resource = await SendAndReadAsync(request, cancellationToken);
        return ToSnapshot(resource);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var query =
            $"From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toUtc)}" +
            $"&PageSize={PageSize}";

        var next = MessagesCollectionUri() + "?" + query;
        var results = new List<TwilioMessageSnapshot>();

        while (!string.IsNullOrEmpty(next))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveMessagingUri(next));
            AddBasicAuth(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            MessageListResponse? page;
            try
            {
                page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Provider message list payload could not be parsed.");
                throw new InvalidOperationException("The provider message list could not be parsed.");
            }

            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = string.IsNullOrEmpty(page?.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private Uri MessagesCollectionUri()
    {
        return ResolveMessagingUri($"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json");
    }

    private Uri MessageInstanceUri(string messageSid)
    {
        return ResolveMessagingUri($"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
    }

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        string pathAndQuery;
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            pathAndQuery = absolute.PathAndQuery;
        }
        else
        {
            pathAndQuery = uriOrPath.StartsWith('/') ? uriOrPath : "/" + uriOrPath;
        }

        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

        return new Uri(baseUrl + pathAndQuery);
    }

    private void AddBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<MessageResource> SendAndReadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResourceAsync(response, cancellationToken);
    }

    private async Task<MessageResource> ReadResourceAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);

        try
        {
            var resource = JsonSerializer.Deserialize<MessageResource>(payload, JsonOptions);
            if (resource is null || string.IsNullOrEmpty(resource.Sid))
            {
                throw new InvalidOperationException("The provider returned an empty message resource.");
            }

            return resource;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Provider message payload could not be parsed. HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("The provider message payload could not be parsed.");
        }
    }

    private void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = TryReadErrorCode(payload);
        _logger.LogWarning("Provider messaging request failed with HTTP {StatusCode} and error code {ErrorCode}.", (int)response.StatusCode, code);
        throw new InvalidOperationException($"The messaging provider returned HTTP {(int)response.StatusCode} (error {code?.ToString() ?? "unknown"}).");
    }

    private static int? TryReadErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
            {
                return code.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static TwilioMessageSnapshot ToSnapshot(MessageResource resource)
    {
        return new TwilioMessageSnapshot
        {
            Sid = resource.Sid ?? string.Empty,
            Status = resource.Status ?? string.Empty,
            Body = resource.Body,
            To = resource.To,
            From = resource.From,
            ErrorCode = resource.ErrorCode,
            ErrorMessage = resource.ErrorMessage,
            DateSent = resource.DateSent,
            DateCreated = resource.DateCreated
        };
    }

    private sealed class MessageListResponse
    {
        public List<MessageResource>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }
}
