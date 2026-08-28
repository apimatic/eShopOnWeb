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

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient;
    private readonly HttpClient _lookupClient;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        ValidateOptions(_options);
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;

        var authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        _messagingClient = CreateClient(authorization);
        _lookupClient = CreateClient(authorization);
    }

    public async Task<SmsDestinationValidation> ValidateDestinationAsync(string number, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(number)}");
            using var response = await _lookupClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
            {
                return new SmsDestinationValidation(false, null);
            }

            await EnsureSuccessAsync(response, "destination validation", cancellationToken);
            var model = await DeserializeAsync<LookupResponse>(response, "destination validation", cancellationToken);
            return new SmsDestinationValidation(model.Valid && !string.IsNullOrWhiteSpace(model.PhoneNumber), model.PhoneNumber);
        }
        catch (SmsProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SmsProviderException("destination validation", innerException: ex);
        }
    }

    public Task<SmsProviderMessage> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt is not null)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        return PostMessageAsync(MessagesPath(), fields, sendAt is null ? "message send" : "message scheduling", cancellationToken);
    }

    public Task<SmsProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        GetMessageCoreAsync(MessagePath(providerMessageSid), "message status retrieval", cancellationToken);

    public Task<SmsProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        PostMessageAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "scheduled message cancellation",
            cancellationToken);

    public Task<SmsProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        PostMessageAsync(
            MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message content disposal",
            cancellationToken);

    public async Task<IReadOnlyList<SmsProviderMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = string.Join("&", new[]
        {
            Query("From", _options.FromNumber),
            Query("DateSent>", FormatQueryDate(from.AddSeconds(-1))),
            Query("DateSent<", FormatQueryDate(to.AddSeconds(1))),
            Query("PageSize", "1000")
        });

        var nextPath = $"{MessagesPath()}?{query}";
        var messages = new List<SmsProviderMessage>();

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            try
            {
                using var response = await _messagingClient.GetAsync(MessagingUri(nextPath), cancellationToken);
                await EnsureSuccessAsync(response, "message reconciliation", cancellationToken);
                var page = await DeserializeAsync<MessageListResponse>(response, "message reconciliation", cancellationToken);
                messages.AddRange(page.Messages.Select(ToProviderMessage));
                nextPath = page.NextPageUri;
            }
            catch (SmsProviderException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new SmsProviderException("message reconciliation", innerException: ex);
            }
        }

        return messages
            .Where(x => x.DateSent is not null && x.DateSent >= from && x.DateSent <= to)
            .ToList();
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private async Task<SmsProviderMessage> GetMessageCoreAsync(string path, string operation, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _messagingClient.GetAsync(MessagingUri(path), cancellationToken);
            await EnsureSuccessAsync(response, operation, cancellationToken);
            return ToProviderMessage(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
        }
        catch (SmsProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SmsProviderException(operation, innerException: ex);
        }
    }

    private async Task<SmsProviderMessage> PostMessageAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await _messagingClient.PostAsync(MessagingUri(path), content, cancellationToken);
            await EnsureSuccessAsync(response, operation, cancellationToken);
            return ToProviderMessage(await DeserializeAsync<MessageResponse>(response, operation, cancellationToken));
        }
        catch (SmsProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SmsProviderException(operation, innerException: ex);
        }
    }

    private Uri MessagingUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            path = absolute.PathAndQuery;
        }

        return new Uri($"{_messagingBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string Query(string name, string value) =>
        $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private static string FormatQueryDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static HttpClient CreateClient(AuthenticationHeaderValue authorization)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = authorization;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        return result ?? throw new SmsProviderException(operation);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        int? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, cancellationToken: cancellationToken);
            code = error?.Code;
        }
        catch (JsonException)
        {
            // Provider response text may contain PII, so it is intentionally not retained.
        }

        throw new SmsProviderException(operation, code);
    }

    private static SmsProviderMessage ToProviderMessage(MessageResponse model) => new(
        model.Sid ?? string.Empty,
        model.Status ?? "unknown",
        model.From,
        model.To,
        model.Body,
        ParseDate(model.DateCreated),
        ParseDate(model.DateSent),
        ParseDate(model.DateUpdated),
        model.ErrorCode);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private static void ValidateOptions(TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) ||
            string.IsNullOrWhiteSpace(options.AuthToken) ||
            string.IsNullOrWhiteSpace(options.FromNumber) ||
            string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio AccountSid, AuthToken, FromNumber, and MessagingServiceSid must be configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https")))
        {
            throw new InvalidOperationException("Twilio BaseUrl must be an absolute HTTP or HTTPS URL.");
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
