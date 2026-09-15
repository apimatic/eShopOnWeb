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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PhoneNumberLookupResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await SendWithTransientRetryAsync(request, cancellationToken);
        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberLookupResult(payload.Valid, payload.PhoneNumber,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<SmsProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateMessageAsync(to, body, null, cancellationToken);

    public Task<SmsProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateMessageAsync(to, body, sendAt, cancellationToken);

    public Task<SmsProviderMessage> GetAsync(string messageSid, CancellationToken cancellationToken) =>
        GetMessageAsync(HttpMethod.Get, MessageUri(messageSid), null, cancellationToken);

    public Task<SmsProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken) =>
        GetMessageAsync(HttpMethod.Post, MessageUri(messageSid), new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<SmsProviderMessage> DisposeContentAsync(string messageSid, CancellationToken cancellationToken) =>
        GetMessageAsync(HttpMethod.Post, MessageUri(messageSid), new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<SmsProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        string? uri = $"{MessagesUri()}?{BuildQuery(query)}";
        var messages = new List<SmsProviderMessage>();

        while (uri is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, uri);
            using var response = await SendWithTransientRetryAsync(request, cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map));
            uri = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : MessagingUri(page.NextPageUri);
        }

        return messages;
    }

    private async Task<SmsProviderMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body
        };
        if (sendAt.HasValue)
        {
            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }
        return await GetMessageAsync(HttpMethod.Post, MessagesUri(), form, cancellationToken);
    }

    private async Task<SmsProviderMessage> GetMessageAsync(HttpMethod method, string uri,
        IReadOnlyDictionary<string, string>? form, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        using var request = CreateRequest(method, uri);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await SendWithTransientRetryAsync(request, cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private async Task<HttpResponseMessage> SendWithTransientRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpRequestMessage current = attempt == 0 ? request : await CloneAsync(request, cancellationToken);
            try
            {
                var response = await _httpClient.SendAsync(current, cancellationToken);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
                    response.Dispose();
                    throw new SmsProviderException($"Twilio rejected the request with HTTP {(int)response.StatusCode}.", errorCode);
                }
                return response;
            }
            catch (HttpRequestException ex) when (attempt >= 2)
            {
                throw new SmsProviderException("Twilio could not be reached.", null, ex);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
            ?? throw new SmsProviderException("Twilio returned an empty response.");
    }

    private async Task<int?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return (await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, _jsonOptions, cancellationToken))?.Code;
        }
        catch (JsonException) { return null; }
    }

    private string MessagesUri() => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");
    private string MessageUri(string sid) => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json");
    private string MessagingUri(string relativeUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl;
        return $"{baseUrl.TrimEnd('/')}/{relativeUri.TrimStart('/')}";
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> values) => string.Join("&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new SmsProviderException("Twilio credentials are not configured.");
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            throw new SmsProviderException("Twilio messaging settings are not configured.");
    }

    private static SmsProviderMessage Map(MessageResponse message) => new(message.Sid, message.Status,
        message.Body, message.From, message.To, message.ErrorCode, ParseTwilioDate(message.DateCreated), ParseTwilioDate(message.DateSent));

    private static DateTimeOffset? ParseTwilioDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        public string Sid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        public MessageResponse[] Messages { get; set; } = Array.Empty<MessageResponse>();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse { public int? Code { get; set; } }
}
