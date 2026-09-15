using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        TwilioLookupClient.ApplyAuthentication(_httpClient, _options);
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public Task<ProviderMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = BaseMessageFields(to, body);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<ProviderMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = BaseMessageFields(to, body);
        fields["MessagingServiceSid"] = _options.MessagingServiceSid;
        fields["ScheduleType"] = "fixed";
        fields["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<ProviderMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstancePath(providerMessageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = DeserializeMessage(payload);
        if (!response.IsSuccessStatusCode)
        {
            return ToSnapshot(resource, succeeded: false, payload);
        }

        return ToSnapshot(resource, succeeded: true, payload);
    }

    public async Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await UpdateMessageAsync(providerMessageSid, fields, cancellationToken);
    }

    public async Task<ProviderMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        return await UpdateMessageAsync(providerMessageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageSnapshot>> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var messages = new List<ProviderMessageSnapshot>();
        var path = AppendQuery(MessagesCollectionPath(), new Dictionary<string, string?>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        });

        string? next = path;
        while (!string.IsNullOrEmpty(next))
        {
            var requestUri = ResolveMessagingUri(next);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions);
            if (page?.Messages is not null)
            {
                messages.AddRange(page.Messages.Select(resource => ToSnapshot(resource, succeeded: true, payload: null)));
            }

            next = string.IsNullOrWhiteSpace(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return messages;
    }

    private Dictionary<string, string> BaseMessageFields(string to, string body)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
        }

        return fields;
    }

    private async Task<ProviderMessageSnapshot> CreateMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var resource = DeserializeMessage(payload);
        return ToSnapshot(resource, response.IsSuccessStatusCode, payload);
    }

    private async Task<ProviderMessageSnapshot> UpdateMessageAsync(string providerMessageSid, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessageInstancePath(providerMessageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var resource = DeserializeMessage(payload);

        if (!response.IsSuccessStatusCode && TryReadErrorCode(payload) == "30409")
        {
            var current = await FetchAsync(providerMessageSid, cancellationToken);
            if (current is not null)
            {
                return current;
            }
        }

        return ToSnapshot(resource, response.IsSuccessStatusCode, payload);
    }

    private static string AppendQuery(string path, Dictionary<string, string?> values)
    {
        var builder = new StringBuilder(path);
        var separator = path.Contains('?') ? '&' : '?';
        foreach (var pair in values)
        {
            if (pair.Value is null)
            {
                continue;
            }

            builder.Append(separator);
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
            separator = '&';
        }

        return builder.ToString();
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid) =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    private Uri ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(_httpClient.BaseAddress!, absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(_httpClient.BaseAddress!, nextPageUri.TrimStart('/'));
    }

    private static TwilioMessageResource? DeserializeMessage(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code))
            {
                return code.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static ProviderMessageSnapshot ToSnapshot(TwilioMessageResource? resource, bool succeeded, string? payload)
    {
        var errorCode = resource?.ErrorCodeText;
        var errorMessage = resource?.ErrorMessage;
        if (!succeeded && string.IsNullOrEmpty(errorCode) && payload is not null)
        {
            errorCode = TryReadErrorCode(payload);
            errorMessage = TryReadErrorMessage(payload);
        }

        return new ProviderMessageSnapshot
        {
            Succeeded = succeeded && !string.IsNullOrEmpty(resource?.Sid),
            Sid = resource?.Sid,
            Status = resource?.Status,
            Body = resource?.Body,
            From = resource?.From,
            To = resource?.To,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            DateCreated = ParseTwilioDate(resource?.DateCreated),
            DateSent = ParseTwilioDate(resource?.DateSent)
        };
    }

    private static string? TryReadErrorMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
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

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageResource
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

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        public string? ErrorCodeText => ErrorCode.ValueKind switch
        {
            JsonValueKind.Number => ErrorCode.GetRawText(),
            JsonValueKind.String => ErrorCode.GetString(),
            _ => null
        };
    }
}
