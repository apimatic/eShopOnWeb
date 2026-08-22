using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Messaging client for the v2010 Message resource in api-specs/twilio/twilio_api_v2010:
/// CreateMessage, FetchMessage, UpdateMessage, ListMessage.
/// Twilio:BaseUrl, when set, replaces https://api.twilio.com for every call.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string FromNumber => _options.FromNumber;

    public async Task<TwilioMessage> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (!request.SendAt.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(request.From))
            {
                fields.Add(new("From", request.From));
            }
            else if (!string.IsNullOrWhiteSpace(_options.FromNumber))
            {
                fields.Add(new("From", _options.FromNumber));
            }
        }

        var messagingServiceSid = request.MessagingServiceSid;
        if (string.IsNullOrWhiteSpace(messagingServiceSid) && request.SendAt.HasValue)
        {
            messagingServiceSid = _options.MessagingServiceSid;
        }

        if (!string.IsNullOrWhiteSpace(messagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", messagingServiceSid));
        }

        if (!string.IsNullOrWhiteSpace(request.ScheduleType))
        {
            fields.Add(new("ScheduleType", request.ScheduleType));
        }
        else if (request.SendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
        }

        if (request.SendAt.HasValue)
        {
            fields.Add(new("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(httpRequest);

        using var response = await SendAsync(httpRequest, cancellationToken);
        EnsureSuccess(response, "CreateMessage", 201);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(messageSid));
        ApplyBasicAuth(httpRequest);
        using var response = await SendAsync(httpRequest, cancellationToken);
        EnsureSuccess(response, "FetchMessage", 200);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> UpdateMessageAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        var fields = new List<KeyValuePair<string, string>>();
        if (body != null)
        {
            fields.Add(new("Body", body));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            fields.Add(new("Status", status));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(httpRequest);
        using var response = await SendAsync(httpRequest, cancellationToken);
        EnsureSuccess(response, "UpdateMessage", 200);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(string from, DateTimeOffset fromSent, DateTimeOffset toSent, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        var messages = new List<TwilioMessage>();
        var fromUtc = fromSent.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = toSent.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Spec ListMessage examples encode DateSent> / DateSent< as DateSent>= / DateSent<= in the query string.
        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(from)}&DateSent%3E={Uri.EscapeDataString(fromUtc)}&DateSent%3C={Uri.EscapeDataString(toUtc)}&PageSize=1000";

        while (!string.IsNullOrEmpty(path))
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, path);
            ApplyBasicAuth(httpRequest);
            using var response = await SendAsync(httpRequest, cancellationToken);
            EnsureSuccess(response, "ListMessage", 200);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<ListMessageResponseDto>(stream, _jsonOptions, cancellationToken);
            if (page?.Messages != null)
            {
                foreach (var item in page.Messages)
                {
                    messages.Add(ToModel(item));
                }
            }

            path = string.IsNullOrWhiteSpace(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return messages;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null && !request.RequestUri.IsAbsoluteUri)
        {
            request.RequestUri = new Uri(GetBaseUri(), request.RequestUri.OriginalString.TrimStart('/'));
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private Uri GetBaseUri()
    {
        var configured = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultBaseUrl : _options.BaseUrl.TrimEnd('/');
        return new Uri(configured + "/", UriKind.Absolute);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid)
        => $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new TwilioApiException("Twilio AccountSid and AuthToken are not configured.");
        }
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation, int expectedStatus)
    {
        if ((int)response.StatusCode == expectedStatus)
        {
            return;
        }

        if (response.IsSuccessStatusCode && expectedStatus == 201 && (int)response.StatusCode == 200)
        {
            return;
        }

        throw new TwilioApiException($"Messaging API {operation} failed with status {(int)response.StatusCode}.");
    }

    private async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dto = await JsonSerializer.DeserializeAsync<MessageResourceDto>(stream, _jsonOptions, cancellationToken);
        if (dto == null)
        {
            throw new TwilioApiException("Messaging API returned an empty message body.");
        }

        return ToModel(dto);
    }

    private static TwilioMessage ToModel(MessageResourceDto dto) => new()
    {
        Sid = dto.Sid,
        Status = dto.Status,
        Body = dto.Body,
        From = dto.From,
        To = dto.To,
        ErrorCode = dto.ErrorCode,
        ErrorMessage = dto.ErrorMessage,
        DateSent = dto.DateSent,
        DateCreated = dto.DateCreated,
        Direction = dto.Direction,
        MessagingServiceSid = dto.MessagingServiceSid
    };

    private sealed class MessageResourceDto
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
        public string? Direction { get; set; }
        public string? MessagingServiceSid { get; set; }
    }

    private sealed class ListMessageResponseDto
    {
        public List<MessageResourceDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }
}
