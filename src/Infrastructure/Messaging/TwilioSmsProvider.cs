using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsProvider : ISmsProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;

    public TwilioSmsProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All
        });
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var uri = new Uri($"https://lookups.twilio.com/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}");
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return new PhoneNumberValidation(false, null);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberValidation(result.Valid, result.Valid ? result.PhoneNumber : null);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = Required(_options.FromNumber, "FromNumber"),
            ["Body"] = body
        };
        if (sendAt is not null)
        {
            values["MessagingServiceSid"] = Required(_options.MessagingServiceSid, "MessagingServiceSid");
            values["ScheduleType"] = "fixed";
            values["SendAt"] = sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
        return PostMessageAsync(MessagesPath, values, cancellationToken);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(MessagingUri(MessagePath(messageSid)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return Map(await ReadAsync<TwilioMessageResponse>(response, cancellationToken));
    }

    public async Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        ProviderMessage state;
        try
        {
            state = await PostMessageAsync(MessagePath(messageSid),
                new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
        }
        catch (TwilioRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict && ex.ProviderCode == 30409)
        {
            // A preceding cancellation can be accepted before the read model catches up.
            state = await GetMessageAsync(messageSid, cancellationToken);
        }
        if (state.Status.Equals("canceled", StringComparison.OrdinalIgnoreCase)) return state;

        // Twilio may expose the transition asynchronously after accepting the update.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            state = await GetMessageAsync(messageSid, cancellationToken);
            if (state.Status.Equals("canceled", StringComparison.OrdinalIgnoreCase)) return state;
        }

        throw new InvalidOperationException("Twilio did not confirm scheduled-message cancellation.");
    }

    public Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        PostMessageAsync(MessagePath(messageSid), new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        // The classic Messages API only offers whole-day DateSent filters, which omit
        // scheduled/canceled messages whose DateSent is null. Query every page for this
        // application's sender and apply the requested ISO-8601 interval to DateCreated.
        var query = $"From={Uri.EscapeDataString(Required(_options.FromNumber, "FromNumber"))}&PageSize=1000";
        Uri? next = MessagingUri($"{MessagesPath}?{query}");
        var messages = new List<ProviderMessage>();

        while (next is not null)
        {
            using var response = await _httpClient.GetAsync(next, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await ReadAsync<TwilioMessagePage>(response, cancellationToken);
            messages.AddRange(page.Messages.Select(Map));
            next = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : MessagingUri(page.NextPageUri);
        }

        return messages.Where(x =>
        {
            var timestamp = x.DateCreated ?? x.DateSent;
            return timestamp >= from && timestamp <= to;
        }).ToList();
    }

    private string MessagesPath => $"/2010-04-01/Accounts/{Required(_options.AccountSid, "AccountSid")}/Messages.json";
    private string MessagePath(string sid) => $"/2010-04-01/Accounts/{Required(_options.AccountSid, "AccountSid")}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<ProviderMessage> PostMessageAsync(string path, Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var content = new FormUrlEncodedContent(values);
        using var response = await _httpClient.PostAsync(MessagingUri(path), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return Map(await ReadAsync<TwilioMessageResponse>(response, cancellationToken));
    }

    private Uri MessagingUri(string path)
    {
        var configured = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://api.twilio.com" : _options.BaseUrl;
        var baseUri = new Uri(configured!.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private void EnsureCredentials()
    {
        Required(_options.AccountSid, "AccountSid");
        Required(_options.AuthToken, "AuthToken");
    }

    private static string Required(string value, string key) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Twilio:{key} is not configured.") : value;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
        ?? throw new TwilioRequestException(response.StatusCode, null);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        int? code = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(JsonOptions, cancellationToken);
            code = error?.Code;
        }
        catch (JsonException) { }
        throw new TwilioRequestException(response.StatusCode, code);
    }

    private static ProviderMessage Map(TwilioMessageResponse x) => new(
        x.Sid, x.Status, x.From, x.To, x.Body, x.ErrorCode, ParseDate(x.DateCreated), ParseDate(x.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; init; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; init; }
    }

    private sealed class TwilioMessageResponse
    {
        [JsonPropertyName("sid")] public string Sid { get; init; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
        [JsonPropertyName("from")] public string? From { get; init; }
        [JsonPropertyName("to")] public string? To { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; init; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; init; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; init; }
    }

    private sealed class TwilioMessagePage
    {
        [JsonPropertyName("messages")] public List<TwilioMessageResponse> Messages { get; init; } = [];
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; init; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; init; }
    }
}

public sealed class TwilioRequestException : Exception
{
    public TwilioRequestException(HttpStatusCode statusCode, int? providerCode)
        : base($"Twilio request failed with HTTP {(int)statusCode}" + (providerCode is null ? "." : $" and provider code {providerCode}."))
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ProviderCode { get; }
}
