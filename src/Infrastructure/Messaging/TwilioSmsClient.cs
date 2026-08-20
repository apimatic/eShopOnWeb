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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioSmsClient : ITwilioSmsClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsClient> _logger;

    public TwilioSmsClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string FromNumber => _options.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}");
        using var response = await SendAsync(HttpMethod.Get, uri, content: null, cancellationToken);
        var payload = await ReadRequiredAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.ValidationErrors ?? new List<string>());
    }

    public async Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _options.FromNumber)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(HttpMethod.Post, MessagingUri("Messages.json"), content, cancellationToken);
        return ToProviderMessage(await ReadRequiredAsync<MessageResponse>(response, cancellationToken));
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, MessagingUri($"Messages/{Uri.EscapeDataString(messageSid)}.json"), content: null, cancellationToken);
        return ToProviderMessage(await ReadRequiredAsync<MessageResponse>(response, cancellationToken));
    }

    public async Task<ProviderMessage> UpdateAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (body is not null)
        {
            fields.Add(new("Body", body));
        }

        if (!string.IsNullOrEmpty(status))
        {
            fields.Add(new("Status", status));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(HttpMethod.Post, MessagingUri($"Messages/{Uri.EscapeDataString(messageSid)}.json"), content, cancellationToken);
        return ToProviderMessage(await ReadRequiredAsync<MessageResponse>(response, cancellationToken));
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromSenderAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var dateSentAfter = fromUtc.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var dateSentBefore = toUtc.Date.AddDays(1).ToString("yyyy-MM-dd");

        var query = $"From={Uri.EscapeDataString(fromNumber)}&PageSize=1000&DateSent%3E={Uri.EscapeDataString(dateSentAfter)}&DateSent%3C={Uri.EscapeDataString(dateSentBefore)}";
        var next = MessagingUri($"Messages.json?{query}");
        var results = new List<ProviderMessage>();

        while (next is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, next, content: null, cancellationToken);
            var page = await ReadRequiredAsync<MessageListResponse>(response, cancellationToken);
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = ToProviderMessage(message);
                    if (IsInRange(mapped, fromUtc, toUtc))
                    {
                        results.Add(mapped);
                    }
                }
            }

            next = ResolveMessagingUri(page.NextPageUri);
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicToken());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio {Method} to {Host} failed before a response was received.", method, uri.Host);
            throw;
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = (int)response.StatusCode;
        var error = await TryReadErrorAsync(response, cancellationToken);
        response.Dispose();
        throw new TwilioClientException(
            statusCode,
            error?.Code,
            $"Twilio request failed with HTTP {statusCode} code {error?.Code}.");
    }

    private string BuildBasicToken()
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
    }

    private Uri MessagingUri(string relativePathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl.TrimEnd('/');
        var path = relativePathAndQuery.StartsWith('/') ? relativePathAndQuery : "/" + relativePathAndQuery;
        if (!path.StartsWith("/2010-04-01/", StringComparison.Ordinal))
        {
            path = $"/2010-04-01/Accounts/{_options.AccountSid}/{relativePathAndQuery.TrimStart('/')}";
        }

        return new Uri(baseUrl + path);
    }

    private Uri? ResolveMessagingUri(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl.TrimEnd('/');

        if (nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = new Uri(nextPageUri);
            return new Uri(baseUrl + parsed.PathAndQuery);
        }

        var path = nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri;
        return new Uri(baseUrl + path);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new TwilioClientException((int)response.StatusCode, null, "Twilio returned an empty response body.");
        }

        return value;
    }

    private static async Task<ErrorResponse?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessage ToProviderMessage(MessageResponse message) =>
        new(message.Sid, message.Status ?? "unknown", message.Body, message.To, message.From, message.ErrorCode, message.DateSent, message.DateCreated);

    private static bool IsInRange(ProviderMessage message, DateTimeOffset from, DateTimeOffset to)
    {
        if (TryParseTimestamp(message.DateSent, out var sent))
        {
            return sent >= from && sent <= to;
        }

        if (TryParseTimestamp(message.DateCreated, out var created))
        {
            return created >= from && created <= to;
        }

        return true;
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeUniversal,
            out timestamp);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResponse>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        public int? Code { get; set; }
        public int? Status { get; set; }
    }
}
