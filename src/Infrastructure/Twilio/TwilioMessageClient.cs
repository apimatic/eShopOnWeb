using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSettings = Microsoft.eShopWeb.TwilioSettings;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessageClient : ITwilioMessageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
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

    public async Task<TwilioMessageSnapshot?> CreateMessageAsync(TwilioCreateMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (!string.IsNullOrWhiteSpace(request.From))
        {
            fields.Add(new("From", request.From));
        }

        if (!string.IsNullOrWhiteSpace(request.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", request.MessagingServiceSid));
        }

        if (!string.IsNullOrWhiteSpace(request.ScheduleType))
        {
            fields.Add(new("ScheduleType", request.ScheduleType));
        }

        if (request.SendAt.HasValue)
        {
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(HttpMethod.Post, MessagesCollectionPath(), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync("CreateMessage", response, cancellationToken);
            return null;
        }

        var resource = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio CreateMessage succeeded with sid {MessageSid} and status {Status}", resource?.Sid, resource?.Status);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, MessageInstancePath(messageSid), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync("FetchMessage", response, cancellationToken);
            return null;
        }

        return ToSnapshot(await ReadMessageAsync(response, cancellationToken));
    }

    public async Task<TwilioMessageSnapshot?> UpdateMessageAsync(string messageSid, TwilioUpdateMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (request.Body != null)
        {
            fields.Add(new("Body", request.Body));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            fields.Add(new("Status", request.Status));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(HttpMethod.Post, MessageInstancePath(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync("UpdateMessage", response, cancellationToken);
            return null;
        }

        var resource = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio UpdateMessage succeeded for sid {MessageSid} with status {Status}", resource?.Sid, resource?.Status);
        return ToSnapshot(resource);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var relative = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
                       $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
                       $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
                       "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(relative))
        {
            using var response = await SendAsync(HttpMethod.Get, relative.TrimStart('/'), content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync("ListMessage", response, cancellationToken);
                break;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<ListMessageResponse>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message)!);
                }
            }

            relative = string.IsNullOrWhiteSpace(page?.NextPageUri)
                ? null
                : TrimToRelative(page!.NextPageUri!);
        }

        _logger.LogInformation("Twilio ListMessage returned {Count} messages for the requested range", results.Count);
        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, HttpContent? content, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, relativePath);
        ApplyAuth(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (content != null)
        {
            request.Content = content;
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static string TrimToRelative(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private async Task<TwilioMessageResource?> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
    }

    private async Task LogFailedResponseAsync(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var sanitized = RedactPossibleSecrets(body);
        _logger.LogWarning("Twilio {Operation} returned {StatusCode}: {Body}", operation, (int)response.StatusCode, sanitized);
    }

    private static string RedactPossibleSecrets(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var redacted = Regex.Replace(body, @"\+?\d{8,15}", "[redacted]");
        return redacted.Length > 500 ? redacted[..500] : redacted;
    }

    private static TwilioMessageSnapshot? ToSnapshot(TwilioMessageResource? resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new TwilioMessageSnapshot
        {
            Sid = resource.Sid,
            Status = resource.Status,
            Body = resource.Body,
            To = resource.To,
            From = resource.From,
            DateSent = resource.DateSent,
            DateCreated = resource.DateCreated,
            ErrorCode = resource.ErrorCode,
            ErrorMessage = resource.ErrorMessage,
            Uri = resource.Uri,
            MessagingServiceSid = resource.MessagingServiceSid
        };
    }

    private sealed class TwilioMessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Uri { get; set; }
        public string? MessagingServiceSid { get; set; }
    }

    private sealed class ListMessageResponse
    {
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
