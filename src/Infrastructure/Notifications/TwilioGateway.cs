using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

// Hand-written from api-specs/twilio/twilio_api_v2010 and twilio_lookups_v2.
public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TwilioGateway(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken) ||
            string.IsNullOrWhiteSpace(settings.FromNumber) || string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            throw new InvalidOperationException("Twilio configuration is incomplete.");
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) &&
            !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute URI.");
        _settings = settings;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber,
        CancellationToken cancellationToken)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await SendAsync(HttpMethod.Get, BuildUrl(LookupsBaseUrl, path), null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            return new PhoneNumberValidation(false, null);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberValidation(payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber),
            payload.Valid ? payload.PhoneNumber : null);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body)
        };
        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }
        return SendMessageFormAsync(MessageCollectionPath(), fields, cancellationToken);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, MessagingUrl(MessagePath(messageSid)), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageFormAsync(MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageFormAsync(MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["From"] = _settings.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var next = MessageCollectionPath() + "?" + string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var response = await SendAsync(HttpMethod.Get, MessagingUrl(NormalizeProviderPath(next)), null,
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map));
            next = page.NextPageUri;
        }

        // Twilio is queried with both boundaries and the configured From number. This final comparison
        // preserves the endpoint's instant-level semantics if the account returns boundary-day records.
        return messages.Where(x => x.DateSent is null || (x.DateSent >= from && x.DateSent <= to)).ToList();
    }

    private async Task<ProviderMessage> SendMessageFormAsync(string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await SendAsync(HttpMethod.Post, MessagingUrl(path), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri) { Content = content };
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioGatewayException();
        }
        catch (HttpRequestException exception)
        {
            throw new TwilioGatewayException(null, exception);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        int? code = null;
        try
        {
            var error = await DeserializeAsync<ErrorResponse>(response, cancellationToken);
            code = error.Code;
        }
        catch (JsonException) { }
        throw new TwilioGatewayException(code);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
                ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new TwilioGatewayException(null, exception);
        }
    }

    private string MessageCollectionPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";
    private Uri MessagingUrl(string path) => BuildUrl(string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? DefaultMessagingBaseUrl : _settings.BaseUrl!, path);
    private static Uri BuildUrl(string baseUrl, string path) => new(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));
    private static string NormalizeProviderPath(string pathOrUrl) =>
        Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) ? absolute.PathAndQuery : pathOrUrl;

    private static ProviderMessage Map(MessageResponse x) => new(x.Sid ?? string.Empty, x.Body, x.From, x.To,
        x.Status ?? "unknown", x.ErrorCode, ParseDate(x.DateCreated), ParseDate(x.DateSent));

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var result)
        ? result : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
