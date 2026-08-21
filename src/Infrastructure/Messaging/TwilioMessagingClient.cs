using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const int MaxPageSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        return PostMessageAsync(MessagesCollectionUri(), new FormUrlEncodedContent(fields), cancellationToken);
    }

    public async Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstanceUri(providerSid), cancellationToken);
        return await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public Task<SmsMessageSnapshot> CancelAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Status", "canceled")
        });
        return PostMessageAsync(MessageInstanceUri(providerSid), content, cancellationToken);
    }

    public Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Body", string.Empty)
        });
        return PostMessageAsync(MessageInstanceUri(providerSid), content, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(fromIso)}",
            $"DateSent%3C={Uri.EscapeDataString(toIso)}",
            $"PageSize={MaxPageSize}"
        });

        var nextUri = $"{MessagingRoot()}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json?{query}";
        var results = new List<SmsMessageSnapshot>();

        while (!string.IsNullOrEmpty(nextUri))
        {
            using var response = await _httpClient.GetAsync(ResolveMessagingUri(nextUri), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowProviderErrorAsync(response, cancellationToken);
            }

            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(MapMessage));
            }

            nextUri = string.IsNullOrEmpty(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return results;
    }

    private async Task<SmsMessageSnapshot> PostMessageAsync(string uri, HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(uri, content, cancellationToken);
        return await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    private async Task<SmsMessageSnapshot> ReadMessageOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderErrorAsync(response, cancellationToken);
        }

        var payload = await DeserializeAsync<TwilioMessageDto>(response, cancellationToken);
        if (payload == null || string.IsNullOrEmpty(payload.Sid))
        {
            throw new TwilioRequestException((int)response.StatusCode, "The provider returned an empty message resource.");
        }

        return MapMessage(payload);
    }

    private async Task ThrowProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int? providerCode = null;
        try
        {
            var error = await DeserializeAsync<TwilioErrorDto>(response, cancellationToken);
            providerCode = error?.Code;
        }
        catch (Exception)
        {
            // The error body is not required to throw, and may include destination details.
        }

        _logger.LogWarning("Twilio Messaging API returned HTTP {StatusCode} (provider code {ProviderCode})",
            (int)response.StatusCode, providerCode);
        throw new TwilioRequestException((int)response.StatusCode, "Twilio messaging request failed.", providerCode);
    }

    private static SmsMessageSnapshot MapMessage(TwilioMessageDto dto)
    {
        return new SmsMessageSnapshot
        {
            Sid = dto.Sid ?? string.Empty,
            Status = dto.Status ?? string.Empty,
            ErrorCode = dto.ErrorCode,
            ErrorMessage = dto.ErrorMessage,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            DateSent = ParseTwilioDate(dto.DateSent),
            DateCreated = ParseTwilioDate(dto.DateCreated)
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

    private string MessagesCollectionUri() =>
        $"{MessagingRoot()}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstanceUri(string sid) =>
        $"{MessagingRoot()}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string MessagingRoot()
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return DefaultMessagingBaseUrl;
        }

        return _settings.BaseUrl.TrimEnd('/');
    }

    private string ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return $"{MessagingRoot()}{absolute.PathAndQuery}";
        }

        if (nextPageUri.StartsWith('/'))
        {
            return $"{MessagingRoot()}{nextPageUri}";
        }

        return $"{MessagingRoot()}/{nextPageUri}";
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private sealed class MessageListResponse
    {
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        public int Code { get; set; }
        public int Status { get; set; }
    }
}
