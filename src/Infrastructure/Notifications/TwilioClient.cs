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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// A deliberately small client for the operations defined by twilio_api_v2010.yaml and
/// twilio_lookups_v2.yaml. No vendor SDK is used.
/// </summary>
public sealed class TwilioClient : IMessageProvider, IPhoneNumberValidator, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioClient(IOptions<TwilioOptions> options, HttpMessageHandler? messagingHandler = null,
        HttpMessageHandler? lookupHandler = null)
    {
        _options = options.Value;
        // Constructed directly so ASP.NET's HttpClient logging handlers cannot log a lookup URL
        // containing a shopper's phone number.
        _messagingClient = new HttpClient(messagingHandler ?? new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _lookupClient = new HttpClient(lookupHandler ?? new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new(false, null, null, "A phone number is required.");
        }

        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupBaseUrl + path));
        using var response = await SendSafelyAsync(_lookupClient, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            {
                return new(false, null, null, error);
            }

            throw new NotificationProviderException($"Twilio Lookups rejected the request ({(int)response.StatusCode}): {error}");
        }

        var payload = await ReadJsonAsync<LookupResponse>(response, cancellationToken);
        return payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber)
            ? new(true, payload.PhoneNumber, payload.CountryCode, null)
            : new(false, null, payload.CountryCode,
                payload.ValidationErrors is { Count: > 0 }
                    ? string.Join(", ", payload.ValidationErrors)
                    : "Twilio does not consider this a valid destination.");
    }

    public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
                throw new NotificationProviderException("Twilio:MessagingServiceSid is required for scheduled messages.");
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesPath, fields, cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(providerMessageId), null, cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendFormAsync(HttpMethod.Post, MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default) =>
        SendFormAsync(HttpMethod.Post, MessagePath(providerMessageId),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        var query = $"From={Uri.EscapeDataString(_options.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}" +
                    $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}" +
                    "&PageSize=1000";
        string? path = MessagesPath + "?" + query;
        var all = new List<ProviderMessage>();

        while (path is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(path));
            using var response = await SendSafelyAsync(_messagingClient, request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await ReadJsonAsync<MessageListResponse>(response, cancellationToken);
            all.AddRange(page.Messages.Select(Map));
            path = NormalizeNextPage(page.NextPageUri);
        }

        return all;
    }

    private string MessagesPath => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string id) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(id)}.json";

    private async Task<ProviderMessage> SendFormAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await SendMessageRequestAsync(method, path, content, cancellationToken);
    }

    private async Task<ProviderMessage> SendMessageRequestAsync(HttpMethod method, string path,
        HttpContent? content, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var request = CreateRequest(method, MessagingUri(path));
        request.Content = content;
        using var response = await SendSafelyAsync(_messagingClient, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return Map(await ReadJsonAsync<MessageResponse>(response, cancellationToken));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri MessagingUri(string path)
    {
        var configuredBase = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        return new Uri(configuredBase!.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
    }

    private static string? NormalizeNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri)) return null;
        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPageUri;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
            throw new NotificationProviderException("Twilio credentials are not configured.");
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
            throw new NotificationProviderException("Twilio:FromNumber is not configured.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var error = await ReadErrorAsync(response, cancellationToken);
        throw new NotificationProviderException($"Twilio rejected the request ({(int)response.StatusCode}): {error}");
    }

    private static async Task<HttpResponseMessage> SendSafelyAsync(HttpClient client, HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NotificationProviderException("Twilio did not respond before the request timeout.");
        }
        catch (HttpRequestException)
        {
            throw new NotificationProviderException("Twilio could not be reached.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<TwilioError>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return string.IsNullOrWhiteSpace(error?.Message) ? response.ReasonPhrase ?? "Provider error" : error.Message;
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "Provider error";
        }
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
               ?? throw new NotificationProviderException("Twilio returned an empty response.");
    }

    private static ProviderMessage Map(MessageResponse message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
            throw new NotificationProviderException("Twilio returned a message without an identifier.");
        return new(message.Sid, message.Status ?? "unknown", message.From, message.To, message.Body,
            ParseDate(message.DateCreated) ?? DateTimeOffset.UtcNow, ParseDate(message.DateSent),
            message.ErrorCode, message.ErrorMessage);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
