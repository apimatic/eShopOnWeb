using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, TwilioSettings settings, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}");
        TwilioHttp.ApplyBasicAuth(request, _settings);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new List<string> { "NOT_A_NUMBER" } };
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio Lookup returned {StatusCode}: {Message}",
                (int)response.StatusCode,
                PiiRedactor.Redact(TwilioHttp.ReadErrorMessage(payload)));
            throw new TwilioRestException((int)response.StatusCode, TwilioHttp.ReadErrorMessage(payload));
        }

        var lookup = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions);
        if (lookup is null)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new List<string> { "NOT_A_NUMBER" } };
        }

        return new PhoneNumberLookupResult
        {
            Valid = lookup.Valid,
            CanonicalPhoneNumber = lookup.PhoneNumber,
            ValidationErrors = lookup.ValidationErrors ?? new List<string>()
        };
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, TwilioSettings settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<TwilioMessageSnapshot> CreateMessageAsync(TwilioCreateMessageRequest request, CancellationToken cancellationToken = default)
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

        if (!string.IsNullOrWhiteSpace(request.ScheduleType) && request.SendAt.HasValue)
        {
            fields.Add(new("ScheduleType", request.ScheduleType));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildMessagingUri(MessageCollectionPath()))
        {
            Content = TwilioHttp.FormContent(fields)
        };
        TwilioHttp.ApplyBasicAuth(httpRequest, _settings);

        return await SendForMessageAsync(httpRequest, cancellationToken, expectedCreated: true);
    }

    public async Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, BuildMessagingUri(MessageInstancePath(messageSid)));
        TwilioHttp.ApplyBasicAuth(httpRequest, _settings);
        return await SendForMessageAsync(httpRequest, cancellationToken, expectedCreated: false);
    }

    public async Task<TwilioMessageSnapshot> UpdateMessageAsync(string messageSid, TwilioUpdateMessageRequest request, CancellationToken cancellationToken = default)
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildMessagingUri(MessageInstancePath(messageSid)))
        {
            Content = TwilioHttp.FormContent(fields)
        };
        TwilioHttp.ApplyBasicAuth(httpRequest, _settings);
        return await SendForMessageAsync(httpRequest, cancellationToken, expectedCreated: false);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset sentFromInclusive,
        DateTimeOffset sentToInclusive,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var pathAndQuery =
            $"{MessageCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(sentFromInclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
            $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(sentToInclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}" +
            "&PageSize=1000";

        while (!string.IsNullOrEmpty(pathAndQuery))
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, BuildMessagingUri(ResolveMessagingPath(pathAndQuery)));
            TwilioHttp.ApplyBasicAuth(httpRequest, _settings);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Twilio ListMessage returned {StatusCode}: {Message}",
                    (int)response.StatusCode,
                    PiiRedactor.Redact(TwilioHttp.ReadErrorMessage(payload)));
                throw new TwilioRestException((int)response.StatusCode, TwilioHttp.ReadErrorMessage(payload));
            }

            var page = JsonSerializer.Deserialize<MessageListDto>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            pathAndQuery = string.IsNullOrEmpty(page?.NextPageUri) ? null : ExtractPathAndQuery(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> SendForMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool expectedCreated)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var success = expectedCreated
            ? response.StatusCode == System.Net.HttpStatusCode.Created
            : response.IsSuccessStatusCode;

        if (!success)
        {
            _logger.LogWarning(
                "Twilio Message API returned {StatusCode}: {Message}",
                (int)response.StatusCode,
                PiiRedactor.Redact(TwilioHttp.ReadErrorMessage(payload)));
            throw new TwilioRestException((int)response.StatusCode, TwilioHttp.ReadErrorMessage(payload));
        }

        var message = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions);
        if (message is null)
        {
            throw new TwilioRestException((int)response.StatusCode, "The provider returned an empty message resource.");
        }

        return ToSnapshot(message);
    }

    private Uri BuildMessagingUri(string relativePathAndQuery)
    {
        var baseUrl = (_httpClient.BaseAddress?.ToString() ?? "https://api.twilio.com/").TrimEnd('/');
        var relative = relativePathAndQuery.TrimStart('/');
        return new Uri($"{baseUrl}/{relative}");
    }

    private string MessageCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static string ResolveMessagingPath(string pathAndQuery)
    {
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return pathAndQuery.TrimStart('/');
    }

    private static string ExtractPathAndQuery(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private static TwilioMessageSnapshot ToSnapshot(MessageDto message) => new()
    {
        Sid = message.Sid,
        Status = message.Status,
        Body = message.Body,
        From = message.From,
        To = message.To,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        DateSent = message.DateSent,
        DateCreated = message.DateCreated,
        Direction = message.Direction,
        MessagingServiceSid = message.MessagingServiceSid,
        AccountSid = message.AccountSid
    };

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
        public string? Direction { get; set; }
        public string? MessagingServiceSid { get; set; }
        public string? AccountSid { get; set; }
    }
}

internal static class TwilioHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static ByteArrayContent FormContent(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var encoded = string.Join("&", fields.Select(field =>
            $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));
        var bytes = Encoding.ASCII.GetBytes(encoded);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    public static void ApplyBasicAuth(HttpRequestMessage request, TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public static string ReadErrorMessage(string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
            if (error != null && (!string.IsNullOrEmpty(error.Message) || error.Code != 0))
            {
                return $"Twilio error {error.Code}: {error.Message}";
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic description; never return the raw payload (may contain numbers).
        }

        return "The messaging provider returned an error.";
    }

    private sealed class TwilioErrorDto
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public int Status { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }
    }
}

public sealed class TwilioRestException : Exception
{
    public TwilioRestException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
