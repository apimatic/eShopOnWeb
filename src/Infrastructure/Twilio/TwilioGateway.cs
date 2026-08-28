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

public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TwilioGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;

        // Deliberately not created through IHttpClientFactory: its standard request logging
        // would record the phone number embedded in a Lookup URL.
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        }) { Timeout = TimeSpan.FromSeconds(15) };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(LookupBaseUrl, path));
        using var response = await SendAsync(request, "phone-number validation", cancellationToken);
        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberValidation(payload.Valid, payload.Valid ? payload.PhoneNumber : null);
    }

    public Task<ProviderMessage> SendMessageAsync(string destination, string content, CancellationToken cancellationToken) =>
        CreateMessageAsync(destination, content, null, cancellationToken);

    public Task<ProviderMessage> ScheduleMessageAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateMessageAsync(destination, content, sendAt, cancellationToken);

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageUri(providerMessageSid));
        using var response = await SendAsync(request, "message fetch", cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, "message cancellation", cancellationToken);

    public Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, "message redaction", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Twilio's list filters operate on whole GMT dates and the raw >/< keys are
        // exclusive. Bracket the requested interval, then enforce exact timestamps below.
        var startDate = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = startDate,
            ["DateSent<"] = endDate,
            ["PageSize"] = "1000"
        });
        var queryString = await query.ReadAsStringAsync(cancellationToken);
        var nextPath = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{queryString}";
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MessagingUri(nextPath));
            using var response = await SendAsync(request, "message reconciliation", cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map).Where(message =>
            {
                var timestamp = message.DateSent ?? message.DateCreated;
                return timestamp >= from && timestamp <= to;
            }));
            nextPath = page.NextPageUri;
        }

        return messages;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ProviderMessage> CreateMessageAsync(string destination, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = destination,
            ["From"] = _options.FromNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = content
        };

        if (sendAt.HasValue)
        {
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUri()) { Content = new FormUrlEncodedContent(fields) };
        using var response = await SendAsync(request, sendAt.HasValue ? "message scheduling" : "message send", cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private async Task<ProviderMessage> UpdateMessageAsync(string sid, Dictionary<string, string> fields, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageUri(sid)) { Content = new FormUrlEncodedContent(fields) };
        using var response = await SendAsync(request, operation, cancellationToken);
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioProviderException(operation);
        }
        catch (HttpRequestException)
        {
            throw new TwilioProviderException(operation);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
        response.Dispose();
        throw new TwilioProviderException(operation, errorCode);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
                ?? throw new TwilioProviderException("response parsing");
        }
        catch (JsonException)
        {
            throw new TwilioProviderException("response parsing");
        }
    }

    private static async Task<int?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioError>(stream, cancellationToken: cancellationToken);
            return error?.Code;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private ProviderMessage Map(MessageResponse message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid) || string.IsNullOrWhiteSpace(message.Status))
        {
            throw new TwilioProviderException("response parsing");
        }

        return new ProviderMessage(message.Sid, message.Status, message.ErrorCode,
            ParseDate(message.DateCreated), ParseDate(message.DateSent));
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private Uri MessagesUri() => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");
    private Uri MessageUri(string sid) => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json");
    private Uri MessagingUri(string path)
    {
        EnsureConfigured();
        if (Uri.TryCreate(path, UriKind.Absolute, out var providerUri))
        {
            path = providerUri.PathAndQuery;
        }
        return Combine(string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl!, path);
    }

    private static Uri Combine(string baseUrl, string path)
    {
        var separator = baseUrl.EndsWith('/') || path.StartsWith('/') ? string.Empty : "/";
        if (baseUrl.EndsWith('/') && path.StartsWith('/'))
        {
            path = path[1..];
        }
        return new Uri(baseUrl + separator + path, UriKind.Absolute);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new TwilioProviderException("configuration");
        }
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse
    {
        public string Sid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        public int? Code { get; set; }
    }
}
