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

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway, IDisposable
{
    private static readonly Uri LookupBase = new("https://lookups.twilio.com/");
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _messagingBase;

    public TwilioMessagingGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        RequireConfiguration();
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.twilio.com/"
            : _options.BaseUrl!;
        _messagingBase = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/', UriKind.Absolute);
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string rawNumber, string? countryCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            return new(false, null, new[] { "NOT_A_NUMBER" });

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.ToUpperInvariant())}";
        HttpResponseMessage response;
        try { response = await _httpClient.GetAsync(new Uri(LookupBase, path), cancellationToken); }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new TwilioProviderException("Phone-number validation is temporarily unavailable.");
        }
        using (response)
        {
        if (!response.IsSuccessStatusCode)
            throw new TwilioProviderException("Phone-number validation is temporarily unavailable.", (int)response.StatusCode);
        var dto = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        return new(dto.Valid && !string.IsNullOrWhiteSpace(dto.PhoneNumber), dto.PhoneNumber,
            dto.ValidationErrors ?? Array.Empty<string>());
        }
    }

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };
        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }
        return SendFormAsync(HttpMethod.Post, MessagesPath(), values, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try { response = await _httpClient.GetAsync(MessagingUri(MessagePath(messageSid)), cancellationToken); }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new TwilioProviderException("The provider messaging service is temporarily unavailable.");
        }
        using (response)
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendFormAsync(HttpMethod.Post, MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<ProviderMessage> RedactAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendFormAsync(HttpMethod.Post, MessagePath(messageSid), new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", _options.FromNumber),
            // Twilio's comparison keys operate on day boundaries. Widen by one day,
            // then apply the caller's exact DateTimeOffset range below.
            new("DateSent>", from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("PageSize", "1000")
        };
        var next = MessagesPath() + "?" + EncodeQuery(query);
        var result = new List<ProviderMessage>();

        while (next is not null)
        {
            HttpResponseMessage response;
            try { response = await _httpClient.GetAsync(MessagingUri(next), cancellationToken); }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                throw new TwilioProviderException("The provider message list could not be read.");
            }
            using (response)
            {
            if (!response.IsSuccessStatusCode)
                throw new TwilioProviderException("The provider message list could not be read.", (int)response.StatusCode);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            result.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            next = NextRelativePath(page.NextPageUri);
            }
        }

        return result.Where(x => x.DateSent >= from && x.DateSent <= to).ToList();
    }

    private async Task<ProviderMessage> SendFormAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>> values, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, MessagingUri(path))
        {
            Content = new FormUrlEncodedContent(values)
        };
        HttpResponseMessage response;
        try { response = await _httpClient.SendAsync(request, cancellationToken); }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new TwilioProviderException("The provider messaging service is temporarily unavailable.");
        }
        using (response)
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await TryDeserializeAsync<ProviderError>(response, cancellationToken);
            throw new TwilioProviderException("The provider rejected the messaging request.",
                (int)response.StatusCode, error?.Code);
        }
        return Map(await DeserializeAsync<MessageResponse>(response, cancellationToken));
    }

    private static ProviderMessage Map(MessageResponse dto) => new(
        dto.Sid ?? throw new TwilioProviderException("The provider response did not contain a message identifier."),
        dto.Status ?? "unknown", dto.From, dto.To, dto.Body, dto.ErrorCode,
        ParseProviderDate(dto.DateCreated), ParseProviderDate(dto.DateSent));

    private Uri MessagingUri(string relative) => new(_messagingBase, relative.TrimStart('/'));
    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string? NextRelativePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri)) return null;
        var providerUri = new Uri(nextPageUri, UriKind.RelativeOrAbsolute);
        var query = providerUri.IsAbsoluteUri ? providerUri.Query : new Uri(LookupBase, providerUri).Query;
        // Rebuild against the configured messaging base so an override governs every page.
        return MessagesPath() + query;
    }

    private static string EncodeQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken)
                ?? throw new TwilioProviderException("The provider returned an invalid response.");
        }
        catch (JsonException)
        {
            throw new TwilioProviderException("The provider returned an invalid response.");
        }
    }

    private static async Task<T?> TryDeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try { return await DeserializeAsync<T>(response, cancellationToken); }
        catch { return default; }
    }

    private void RequireConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            throw new InvalidOperationException("Twilio configuration is incomplete.");
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    public void Dispose() => _httpClient.Dispose();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        public MessageResponse[]? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class ProviderError { public int? Code { get; set; } }
}

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, int? httpStatus = null, int? providerCode = null) : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }
    public int? HttpStatus { get; }
    public int? ProviderCode { get; }
}
