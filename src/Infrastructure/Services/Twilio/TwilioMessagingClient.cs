using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio Messaging (api v2010) client built to twilio_api_v2010.yaml:
/// CreateMessage, FetchMessage, UpdateMessage, ListMessage.
/// Twilio:BaseUrl, when set, is used verbatim as the base address for every call.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;
    private readonly Uri _baseAddress;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        var configured = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? "https://api.twilio.com"
            : _settings.BaseUrl.TrimEnd('/');
        _baseAddress = new Uri(configured + "/");
        _httpClient.BaseAddress = _baseAddress;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<TwilioMessageSnapshot?> SendSmsAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot?> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = MessageInstancePath(messageSid);
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetching message {MessageSid} from the provider failed with status {StatusCode}.", messageSid, (int)response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<MessageResourceDto>(JsonOptions, cancellationToken);
        return dto is null ? null : ToSnapshot(dto);
    }

    public Task<TwilioMessageSnapshot?> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        return UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot?> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };
        return UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var fromUtc = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var toUtc = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromUtc)}" +
            $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toUtc)}" +
            "&PageSize=1000";

        var next = MessagesListPath() + "?" + query;

        while (!string.IsNullOrWhiteSpace(next))
        {
            var requestUri = ResolveMessagingUri(next);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Listing provider messages failed with status {StatusCode}.", (int)response.StatusCode);
                break;
            }

            var page = await response.Content.ReadFromJsonAsync<ListMessageResponseDto>(JsonOptions, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = page?.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot?> CreateMessageAsync(
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagesListPath(), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CreateMessage failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<MessageResourceDto>(JsonOptions, cancellationToken);
        return dto is null ? null : ToSnapshot(dto);
    }

    private async Task<TwilioMessageSnapshot?> UpdateMessageAsync(
        string messageSid,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("UpdateMessage {MessageSid} failed with status {StatusCode}.", messageSid, (int)response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<MessageResourceDto>(JsonOptions, cancellationToken);
        return dto is null ? null : ToSnapshot(dto);
    }

    private string MessagesListPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri(_baseAddress, absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(_baseAddress, uriOrPath.TrimStart('/'));
    }

    private static TwilioMessageSnapshot ToSnapshot(MessageResourceDto dto) =>
        new(dto.Sid, dto.Status, dto.ErrorCode, dto.ErrorMessage, dto.Body, dto.From, dto.To, dto.DateCreated, dto.DateSent);
}
