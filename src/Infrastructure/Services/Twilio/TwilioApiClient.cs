using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiClient : ITwilioLookupClient, ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioApiClient> _logger;

    public TwilioApiClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{TwilioSettings.LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup returned {StatusCode}.", (int)response.StatusCode);
            throw new TwilioApiException((int)response.StatusCode, "The provider could not look up the number.");
        }

        var dto = JsonSerializer.Deserialize<LookupDto>(payload, JsonOptions)
            ?? throw new TwilioApiException(500, "The provider returned an empty lookup response.");

        return new TwilioLookupResult(
            dto.Valid,
            dto.PhoneNumber,
            dto.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    public Task<TwilioMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = ImmediateForm(to, body);
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<TwilioMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var form = ImmediateForm(to, body);
        form["MessagingServiceSid"] = _settings.MessagingServiceSid;
        form["ScheduleType"] = "fixed";
        form["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<TwilioMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ToAbsoluteUri(MessagePath(messageSid)), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessageResult>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageResult>();
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var toIso = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var relative =
            $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json" +
            $"?From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
            "&PageSize=100";

        while (!string.IsNullOrEmpty(relative))
        {
            using var response = await _httpClient.GetAsync(ToAbsoluteUri(relative), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio message list returned {StatusCode}.", (int)response.StatusCode);
                throw new TwilioApiException((int)response.StatusCode, "The provider could not list messages.");
            }

            var page = JsonSerializer.Deserialize<MessageListDto>(payload, JsonOptions)
                ?? new MessageListDto();
            foreach (var message in page.Messages ?? new List<MessageDto>())
            {
                results.Add(ToResult(message));
            }

            relative = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private Dictionary<string, string> ImmediateForm(string to, string body)
    {
        EnsureConfigured();
        return new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
    }

    private async Task<TwilioMessageResult> CreateMessageAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(ToAbsoluteUri(MessagesCollectionPath()), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageResult> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(ToAbsoluteUri(MessagePath(messageSid)), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageResult> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            _logger.LogWarning("Twilio messaging call returned {StatusCode} ({Code}).",
                (int)response.StatusCode, error?.Code);
            throw new TwilioApiException(
                (int)response.StatusCode,
                error?.Message ?? "The messaging provider returned an error.",
                error?.Code);
        }

        var dto = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions)
            ?? throw new TwilioApiException(500, "The provider returned an empty message response.");
        return ToResult(dto);
    }

    private static TwilioMessageResult ToResult(MessageDto dto) =>
        new(dto.Sid, dto.Status ?? "unknown", dto.ErrorCode, dto.ErrorMessage, dto.Body, dto.DateSent, dto.DateCreated, dto.From);

    private ErrorDto? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessagePath(string messageSid) =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private Uri ToAbsoluteUri(string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            if (!string.Equals(absolute.GetLeftPart(UriPartial.Authority),
                    new Uri(_settings.MessagingBaseUrl).GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return new Uri($"{_settings.MessagingBaseUrl}{absolute.PathAndQuery}");
            }

            return absolute;
        }

        var pathAndQuery = relativeOrAbsolute;
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var asAbsolute))
        {
            pathAndQuery = asAbsolute.PathAndQuery;
        }

        return new Uri($"{_settings.MessagingBaseUrl}/{pathAndQuery.TrimStart('/')}");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid)
            || string.IsNullOrWhiteSpace(_settings.AuthToken)
            || string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio settings are not configured.");
        }
    }

    private sealed class LookupDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class MessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Body { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
        public string? From { get; set; }
    }

    private sealed class MessageListDto
    {
        public List<MessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorDto
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public int Status { get; set; }
    }
}

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, string message, int? providerCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
