using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly Uri _baseAddress;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = TwilioAuth.RequireConfigured(options);
        _baseAddress = ResolveBaseAddress(_options.BaseUrl);
        _httpClient.BaseAddress ??= _baseAddress;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public async Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.To);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Body);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _options.FromNumber)
        };

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
            }

            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_options);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await SendAndReadMessageAsync(httpRequest, cancellationToken);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageSid);

        using var request = new HttpRequestMessage(HttpMethod.Get, MessageResourcePath(messageSid));
        request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_options);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await SendAndReadMessageAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNumber);

        var fromValue = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toValue = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var path =
            $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromValue)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toValue)}" +
            "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? next = path;
        var pages = 0;

        while (!string.IsNullOrEmpty(next) && pages < 100)
        {
            pages++;
            using var request = new HttpRequestMessage(HttpMethod.Get, ToRequestUri(next));
            request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_options);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            var page = JsonSerializer.Deserialize<ListMessageResponse>(payload, JsonOptions)
                ?? throw new TwilioApiException((int)response.StatusCode, "Twilio returned an empty message list.");

            if (page.Messages is not null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(ToProviderMessage(item));
                }
            }

            next = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    public async Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        UpdateProviderMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageSid);
        ArgumentNullException.ThrowIfNull(request);

        var fields = new List<KeyValuePair<string, string>>();
        if (request.Body is not null)
        {
            fields.Add(new("Body", request.Body));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            fields.Add(new("Status", request.Status));
        }

        if (fields.Count == 0)
        {
            throw new ArgumentException("An update must include Body or Status.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageResourcePath(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_options);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await SendAndReadMessageAsync(httpRequest, cancellationToken);
    }

    private async Task<ProviderMessage> SendAndReadMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);

        var message = JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions)
            ?? throw new TwilioApiException((int)response.StatusCode, "Twilio returned an empty message.");

        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new TwilioApiException((int)response.StatusCode, "Twilio message response did not include a sid.");
        }

        return ToProviderMessage(message);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new TwilioApiException(
            (int)response.StatusCode,
            $"Twilio Messaging request failed ({(int)response.StatusCode}). {PhoneNumberRedactor.Redact(ExtractErrorMessage(payload))}");
    }

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "Twilio Messaging error";
            }
        }
        catch (JsonException)
        {
            // Fall through — body is not JSON.
        }

        return "Twilio Messaging error";
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageResourcePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private Uri ToRequestUri(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                var configured = ResolveBaseAddress(_options.BaseUrl);
                return new Uri(configured, absolute.PathAndQuery.TrimStart('/'));
            }

            return absolute;
        }

        return new Uri(_baseAddress, pathOrUri.TrimStart('/'));
    }

    public static Uri ResolveBaseAddress(string? configuredBaseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(configuredBaseUrl) ? DefaultBaseUrl : configuredBaseUrl.Trim();
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto)
    {
        return new ProviderMessage(
            dto.Sid ?? string.Empty,
            dto.Status,
            dto.Body,
            dto.From,
            dto.To,
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated),
            dto.ErrorCode,
            dto.ErrorMessage);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class ListMessageResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }
}
