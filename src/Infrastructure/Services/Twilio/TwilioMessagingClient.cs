using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ISmsGateway
{
    private static readonly Uri DefaultMessagingHost = new("https://api.twilio.com");
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s\-().]{6,}\d", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new ProviderUnavailableException("Twilio:MessagingServiceSid is required to schedule a message.");
            }

            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return SendFormAsync(HttpMethod.Post, MessagesCollectionPath(), fields, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildMessagingUri(MessageInstancePath(messageSid)));
        ApplyAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload, messageSid);
        return Map(Deserialize<TwilioMessageDto>(payload));
    }

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), fields, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), fields, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var fromUtc = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toUtc = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var path = MessagesCollectionPath()
                   + "?From=" + Uri.EscapeDataString(fromNumber)
                   + "&DateSent%3E=" + Uri.EscapeDataString(fromUtc)
                   + "&DateSent%3C=" + Uri.EscapeDataString(toUtc)
                   + "&PageSize=1000";

        while (!string.IsNullOrEmpty(path))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildMessagingUri(path));
            ApplyAuth(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload, null);

            var page = Deserialize<TwilioMessageListDto>(payload);
            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(Map));
            }

            path = ResolveNextPagePath(page.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage> SendFormAsync(HttpMethod method, string path, IDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildMessagingUri(path))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload, fields.TryGetValue("To", out _) ? null : null);
        return Map(Deserialize<TwilioMessageDto>(payload));
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new ProviderUnavailableException("Twilio AccountSid and AuthToken are not configured.");
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private Uri BuildMessagingUri(string pathAndQuery)
    {
        var root = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingHost.GetLeftPart(UriPartial.Authority)
            : _settings.BaseUrl.TrimEnd('/');

        var path = pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery;
        return new Uri(root + path, UriKind.Absolute);
    }

    private string MessagesCollectionPath()
    {
        EnsureAccountSid();
        return $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
    }

    private string MessageInstancePath(string messageSid)
    {
        EnsureAccountSid();
        return $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
    }

    private void EnsureAccountSid()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid))
        {
            throw new ProviderUnavailableException("Twilio:AccountSid is not configured.");
        }
    }

    private string? ResolveNextPagePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return nextPageUri;
    }

    private void EnsureSuccess(HttpResponseMessage response, string payload, string? messageSid)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = TryDeserializeError(payload);
        var code = error?.Code;
        _logger.LogWarning(
            "Twilio messaging request failed. StatusCode={StatusCode} ErrorCode={ErrorCode} MessageSid={MessageSid}",
            (int)response.StatusCode,
            code,
            messageSid);

        var safeMessage = Sanitize(error?.Message) ?? "The messaging provider rejected the request.";
        throw new ProviderUnavailableException(safeMessage);
    }

    private static T Deserialize<T>(string payload)
    {
        var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (result is null)
        {
            throw new ProviderUnavailableException("The messaging provider returned an empty response.");
        }

        return result;
    }

    private static TwilioErrorDto? TryDeserializeError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessage Map(TwilioMessageDto dto)
    {
        return new ProviderMessage
        {
            Sid = dto.Sid,
            Status = dto.Status ?? "unknown",
            Body = dto.Body,
            To = dto.To,
            From = dto.From,
            ErrorCode = dto.ErrorCode,
            ErrorMessage = Sanitize(dto.ErrorMessage),
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

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }

    private sealed class TwilioMessageDto
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
