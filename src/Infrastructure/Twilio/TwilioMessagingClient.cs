using System;
using System.Collections.Generic;
using System.Globalization;
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

public class TwilioMessagingClient : IMessagingProviderClient
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageUri(providerSid), fields, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageUri(providerSid));
        ApplyBasicAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch message {Sid} failed with HTTP {StatusCode}.", providerSid, (int)response.StatusCode);
            throw new HttpRequestException($"Twilio fetch failed with HTTP {(int)response.StatusCode}.");
        }

        return MapRequired(DeserializeMessage(payload));
    }

    public async Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUri(providerSid), fields, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var firstUrl =
            $"{MessagesCollectionUri()}?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
            "&PageSize=1000";

        string? nextUrl = firstUrl;
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveMessagingUri(nextUrl));
            ApplyBasicAuth(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new HttpRequestException($"Twilio list failed with HTTP {(int)response.StatusCode}.");
            }

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions) ?? new MessageListResponse();
            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(MapRequired(message));
                }
            }

            nextUrl = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private async Task<ProviderMessage> CreateMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
        => await PostMessageAsync(MessagesCollectionUri(), fields, cancellationToken);

    private async Task<ProviderMessage> PostMessageAsync(string uri, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveMessagingUri(uri));
        ApplyBasicAuth(request);
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Messaging POST failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException($"Twilio messaging request failed with HTTP {(int)response.StatusCode}.");
        }

        return MapRequired(DeserializeMessage(payload));
    }

    private string MessagesCollectionUri()
        => $"{MessagingBaseUrl()}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageUri(string sid)
        => $"{MessagingBaseUrl()}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string MessagingBaseUrl()
    {
        var configured = _options.BaseUrl;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultMessagingBaseUrl;
        }

        return configured.TrimEnd('/');
    }

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            var baseUrl = MessagingBaseUrl();
            if (!string.Equals(absolute.GetLeftPart(UriPartial.Authority), baseUrl, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                return new Uri($"{baseUrl}{absolute.PathAndQuery}");
            }

            return absolute;
        }

        return new Uri($"{MessagingBaseUrl()}{uriOrPath}");
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_options.AccountSid}:{_options.AuthToken}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private static TwilioMessageResource DeserializeMessage(string payload)
        => JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions) ?? new TwilioMessageResource();

    private static ProviderMessage MapRequired(TwilioMessageResource resource)
    {
        if (string.IsNullOrEmpty(resource.Sid) || string.IsNullOrEmpty(resource.Status))
        {
            throw new InvalidOperationException("Twilio message response did not include sid and status.");
        }

        return new ProviderMessage(
            resource.Sid,
            resource.Status,
            resource.ErrorCode,
            resource.Body,
            ParseTwilioDate(resource.DateSent),
            ParseTwilioDate(resource.DateCreated),
            resource.To,
            resource.From);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }
    }
}
