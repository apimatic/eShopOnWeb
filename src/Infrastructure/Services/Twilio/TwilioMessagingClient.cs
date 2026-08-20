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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Programmable Messaging REST client.
/// Confirmed against https://www.twilio.com/docs/messaging/api/message-resource
/// and https://www.twilio.com/docs/messaging/features/message-scheduling
/// Base: POST/GET/POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages[.json|/{Sid}.json]
/// When <see cref="TwilioSettings.BaseUrl"/> is set, that value is used verbatim as the messaging API root.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com/2010-04-01";

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
    }

    public async Task<TwilioMessageSnapshot> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["From"] = _settings.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            values["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        if (sendAt.HasValue)
        {
            // Scheduling requires a Messaging Service: ScheduleType=fixed and SendAt (ISO-8601).
            values["ScheduleType"] = "fixed";
            values["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages.");
            }

            values["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        using var content = new FormUrlEncodedContent(values);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildMessagesCollectionUri())
        {
            Content = content
        };
        ApplyAuth(request);

        var dto = await SendForMessageAsync(request, cancellationToken);
        return ToSnapshot(dto);
    }

    public async Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildMessageUri(messageSid));
        ApplyAuth(request);
        var dto = await SendForMessageAsync(request, cancellationToken);
        return ToSnapshot(dto);
    }

    public async Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // POST Body="" redacts content while leaving the Message resource (status, SID) intact.
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildMessageUri(messageSid))
        {
            Content = content
        };
        ApplyAuth(request);
        var dto = await SendForMessageAsync(request, cancellationToken);
        return ToSnapshot(dto);
    }

    public async Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildMessageUri(messageSid))
        {
            Content = content
        };
        ApplyAuth(request);
        var dto = await SendForMessageAsync(request, cancellationToken);
        return ToSnapshot(dto);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        string? next = BuildListUri(from, to);

        while (!string.IsNullOrEmpty(next))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateRequestException(response.StatusCode, payload);
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, JsonOptions)
                       ?? new TwilioMessageListDto();
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : ResolveNextPageUrl(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageDto> SendForMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(response.StatusCode, payload);
        }

        return JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions) ?? new TwilioMessageDto();
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string MessagingRoot =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

    private string BuildMessagesCollectionUri()
        => $"{MessagingRoot}/Accounts/{_settings.AccountSid}/Messages.json";

    private string BuildMessageUri(string messageSid)
        => $"{MessagingRoot}/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private string BuildListUri(DateTimeOffset from, DateTimeOffset to)
    {
        // Official list filters: From, DateSent>=, DateSent<= (see message-resource docs).
        // PageSize maximum is 1000. Ask Twilio for this FromNumber's messages rather than listing the whole account.
        var fromNumber = Uri.EscapeDataString(_settings.FromNumber);
        var fromSent = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var toSent = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        return $"{BuildMessagesCollectionUri()}?From={fromNumber}&DateSent%3E={fromSent}&DateSent%3C={toSent}&PageSize=1000";
    }

    private string ResolveNextPageUrl(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return CombineRootWithPath(absolute.PathAndQuery);
        }

        return CombineRootWithPath(nextPageUri);
    }

    private string CombineRootWithPath(string pathAndQuery)
    {
        var root = MessagingRoot;
        var path = pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery;
        if (root.EndsWith("/2010-04-01", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/2010-04-01/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["/2010-04-01".Length..];
        }

        return root + path;
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessageDto dto)
        => new(
            dto.Sid ?? string.Empty,
            dto.Status ?? string.Empty,
            dto.Body,
            dto.To,
            dto.From,
            dto.ErrorCode,
            PhoneNumberSanitizer.Redact(dto.ErrorMessage),
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated));

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static Exception CreateRequestException(System.Net.HttpStatusCode statusCode, string payload)
    {
        var sanitized = PhoneNumberSanitizer.Redact(payload);
        string? message = null;
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
            message = PhoneNumberSanitizer.Redact(error?.Message);
        }
        catch (JsonException)
        {
            // fall through
        }

        return new HttpRequestException(
            $"Twilio messaging request failed ({(int)statusCode}): {message ?? sanitized}");
    }

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioMessageListDto
    {
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public int? Status { get; set; }
    }
}
