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

/// <summary>
/// A small client written against api-specs/twilio/twilio_api_v2010 and
/// api-specs/twilio/twilio_lookups_v2. No vendor SDK is used.
/// </summary>
public sealed class TwilioRestClient : ITwilioMessagingClient, ITwilioPhoneNumberValidator, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _messagingBaseUrl;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioRestClient(IOptions<TwilioOptions> options)
        : this(options, new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
    {
    }

    public TwilioRestClient(IOptions<TwilioOptions> options, HttpMessageHandler handler)
    {
        _options = options.Value;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!;
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string number,
        CancellationToken cancellationToken = default)
    {
        EnsureAccountCredentials();
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(number)}";
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberValidationResult(payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber),
            payload.PhoneNumber);
    }

    public async Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMessagingConfiguration();
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };
        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        using var request = CreateRequest(HttpMethod.Post, MessageCollectionUri());
        request.Content = new FormUrlEncodedContent(values);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return Map(await ReadAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendMessageInstanceRequestAsync(messageSid, HttpMethod.Get, null, cancellationToken);

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendMessageInstanceRequestAsync(messageSid, HttpMethod.Post,
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken, retrySafe: true);

    public Task<ProviderMessage> RedactContentAsync(string messageSid,
        CancellationToken cancellationToken = default) =>
        SendMessageInstanceRequestAsync(messageSid, HttpMethod.Post,
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken, retrySafe: true);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureMessagingConfiguration();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var uri = MessageCollectionUri() + "?" + string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var all = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(uri))
        {
            using var request = CreateRequest(HttpMethod.Get, uri);
            using var response = await SendWithSafeRetriesAsync(request, cancellationToken);
            var page = await ReadAsync<MessageListResponse>(response, cancellationToken);
            all.AddRange(page.Messages.Select(Map));
            uri = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? string.Empty
                : MessagingUriFromProviderPage(page.NextPageUri!);
        }

        return all;
    }

    private async Task<ProviderMessage> SendMessageInstanceRequestAsync(string messageSid, HttpMethod method,
        IEnumerable<KeyValuePair<string, string>>? form, CancellationToken cancellationToken, bool retrySafe = false)
    {
        EnsureMessagingConfiguration();
        var uri = MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

        if (!retrySafe)
        {
            using var request = CreateRequest(method, uri);
            if (form != null) request.Content = new FormUrlEncodedContent(form);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return Map(await ReadAsync<MessageResponse>(response, cancellationToken));
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = CreateRequest(method, uri);
                if (form != null) request.Content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                return Map(await ReadAsync<MessageResponse>(response, cancellationToken));
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < 2)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }

        throw last ?? new TwilioProviderException("Twilio request failed.");
    }

    private async Task<HttpResponseMessage> SendWithSafeRetriesAsync(HttpRequestMessage initialRequest,
        CancellationToken cancellationToken)
    {
        // List requests have no body and can be recreated safely.
        var uri = initialRequest.RequestUri!;
        var method = initialRequest.Method;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = CreateRequest(method, uri.ToString());
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if ((int)response.StatusCode < 500 || attempt == 2) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2) { }
            await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string MessageCollectionUri() => MessagingUri(
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");

    private string MessagingUri(string providerPathAndQuery) =>
        $"{_messagingBaseUrl.TrimEnd('/')}/{providerPathAndQuery.TrimStart('/')}";

    private string MessagingUriFromProviderPage(string providerUri)
    {
        if (Uri.TryCreate(providerUri, UriKind.Absolute, out var absolute))
        {
            return MessagingUri(absolute.PathAndQuery);
        }
        return MessagingUri(providerUri);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TwilioError? error = null;
            try { error = JsonSerializer.Deserialize<TwilioError>(content, _jsonOptions); } catch (JsonException) { }
            throw new TwilioProviderException(
                $"Twilio request failed with HTTP {(int)response.StatusCode} and provider code {error?.Code?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.",
                error?.Code, (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, _jsonOptions)
                ?? throw new TwilioProviderException("Twilio returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new TwilioProviderException("Twilio returned an invalid response.", innerException: ex);
        }
    }

    private static ProviderMessage Map(MessageResponse value) => new(
        value.Sid ?? string.Empty, value.Status ?? "unknown", value.Body, value.From, value.To,
        value.ErrorCode, value.ErrorMessage, ParseDate(value.DateCreated), ParseDate(value.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private void EnsureAccountCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new TwilioProviderException("Twilio account credentials are not configured.");
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureAccountCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            throw new TwilioProviderException("Twilio messaging settings are not configured.");
    }

    private static bool IsTransient(Exception exception) => exception is HttpRequestException ||
        exception is TaskCanceledException ||
        exception is TwilioProviderException { HttpStatusCode: 429 or >= 500 } ||
        exception is TwilioProviderException { ProviderCode: null, HttpStatusCode: null };

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
