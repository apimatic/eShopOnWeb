using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioClient : ITwilioClient, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TwilioClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(_options.AccountSid) && !string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken)
    {
        EnsureConfigured(nameof(TwilioOptions.AccountSid), _options.AccountSid);
        EnsureConfigured(nameof(TwilioOptions.AuthToken), _options.AuthToken);

        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(input)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync("phone-number lookup", response, cancellationToken);
        }

        var result = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new ValidatedPhoneNumber(result.Valid, result.PhoneNumber);
    }

    public async Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            EnsureConfigured(nameof(TwilioOptions.MessagingServiceSid), _options.MessagingServiceSid);
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }
        else
        {
            fields.Add(new("From", _options.FromNumber));
        }

        return await SendFormAsync("create message", MessagesPath(), fields, cancellationToken);
    }

    public async Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        using var response = await _httpClient.GetAsync(MessagingUrl(MessagePath(sid)), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync("message lookup", response, cancellationToken);
        }

        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendFormAsync("message cancellation", MessagePath(sid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken) =>
        SendFormAsync("message redaction", MessagePath(sid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        var query = new[]
        {
            Pair("From", _options.FromNumber),
            Pair("DateSent>=", from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("DateSent<=", to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("PageSize", "1000")
        };
        string? pathAndQuery = $"{MessagesPath()}?{string.Join("&", query)}";
        var messages = new List<TwilioMessage>();

        while (pathAndQuery != null)
        {
            using var response = await _httpClient.GetAsync(MessagingUrl(pathAndQuery), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync("message listing", response, cancellationToken);
            }

            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map));
            pathAndQuery = NormalizeNextPage(page.NextPageUri);
        }

        return messages.Where(x => x.DateSent >= from && x.DateSent <= to).ToList();
    }

    private async Task<TwilioMessage> SendFormAsync(string operation, string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagingUrl(path), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(operation, response, cancellationToken);
        }

        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string MessagingUrl(string pathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl;
        return $"{baseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";
    }

    private static string? NormalizeNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri)) return null;
        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPageUri;
    }

    private static string Pair(string key, string value) =>
        $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private void EnsureMessagingConfigured()
    {
        EnsureConfigured(nameof(TwilioOptions.AccountSid), _options.AccountSid);
        EnsureConfigured(nameof(TwilioOptions.AuthToken), _options.AuthToken);
        EnsureConfigured(nameof(TwilioOptions.FromNumber), _options.FromNumber);
    }

    private static void EnsureConfigured(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new TwilioConfigurationException($"Twilio:{name}");
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
            ?? throw new TwilioRequestException("response parsing", (int)response.StatusCode);
    }

    private async Task<TwilioRequestException> CreateExceptionAsync(string operation,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int? code = null;
        try
        {
            var error = await DeserializeAsync<ErrorResponse>(response, cancellationToken);
            code = error.Code;
        }
        catch (Exception) { }

        return new TwilioRequestException(operation, (int)response.StatusCode, code);
    }

    private static TwilioMessage Map(MessageResponse value) => new(
        value.Sid ?? string.Empty,
        value.Status ?? "unknown",
        value.ErrorCode,
        ParseDate(value.DateCreated),
        ParseDate(value.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse> Messages { get; set; } = new();
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
