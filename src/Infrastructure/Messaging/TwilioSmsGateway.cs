using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// HTTP client for Twilio Messaging (api.twilio.com / 2010-04-01) and Lookups v2,
/// built against the OpenAPI documents in api-specs/. No pre-built Twilio SDK.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupsClientName = "TwilioLookups";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string DefaultLookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> options,
        ILogger<TwilioSmsGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // GET /v2/PhoneNumbers/{PhoneNumber} — twilio_lookups_v2.yaml
        var client = _httpClientFactory.CreateClient(LookupsClientName);
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Phone number lookup failed because the provider could not be reached.");
            throw new TwilioApiException("Phone number lookup failed.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Spec: invalid/unknown numbers may 404; treat as not a usable destination.
            if ((int)response.StatusCode == 404)
            {
                return new PhoneNumberLookupResult(false, null);
            }

            _logger.LogWarning("Phone number lookup failed with HTTP {Status}.", (int)response.StatusCode);
            throw new TwilioApiException("Phone number lookup failed.")
            {
                HttpStatus = (int)response.StatusCode,
                TwilioErrorCode = TryReadErrorCode(payload)
            };
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (lookup is null)
        {
            return new PhoneNumberLookupResult(false, null);
        }

        var canonical = string.IsNullOrWhiteSpace(lookup.PhoneNumber) ? null : lookup.PhoneNumber;
        return new PhoneNumberLookupResult(lookup.Valid && canonical != null, canonical);
    }

    public async Task<ProviderMessage> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        // POST /2010-04-01/Accounts/{AccountSid}/Messages.json — CreateMessage
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (request.SendAt.HasValue)
        {
            // ScheduleType=fixed + SendAt, Messaging Service required (spec: message_enum_schedule_type).
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        var response = await SendMessagingAsync(HttpMethod.Post, MessagingPath("Messages.json"), content, cancellationToken);
        return ToProviderMessage(response);
    }

    public async Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json — FetchMessage
        var response = await SendMessagingAsync(HttpMethod.Get, MessageInstancePath(providerMessageSid), content: null, cancellationToken);
        return ToProviderMessage(response);
    }

    public async Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // POST UpdateMessage with Status=canceled. Retry briefly: a message just accepted for
        // scheduling can 404 on update until Twilio's instance resource is fully available.
        TwilioApiException? last = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                var fields = new List<KeyValuePair<string, string>>
                {
                    new("Status", "canceled")
                };
                return ToProviderMessage(await UpdateMessageAsync(providerMessageSid, fields, cancellationToken));
            }
            catch (TwilioApiException ex) when (ex.HttpStatus == 404)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw last ?? new TwilioApiException("Could not cancel the scheduled message.");
    }

    public async Task<ProviderMessage> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // POST UpdateMessage with Body="" (empty string redacts provider-side content)
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };
        return ToProviderMessage(await UpdateMessageAsync(providerMessageSid, fields, cancellationToken));
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // GET ListMessage with From=configured sending number (not a post-filter of a wider list).
        // DateSent> / DateSent< are the spec's range filters (encoded as DateSent>= / DateSent<=).
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var first = MessagingPath("Messages.json")
                    + $"?From={Uri.EscapeDataString(_settings.FromNumber)}"
                    + $"&DateSent%3E={Uri.EscapeDataString(fromIso)}"
                    + $"&DateSent%3C={Uri.EscapeDataString(toIso)}"
                    + "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? next = first;

        while (!string.IsNullOrEmpty(next))
        {
            var requestUri = ResolveMessagingUri(next);
            var list = await SendMessagingListAsync(requestUri, cancellationToken);
            if (list.Messages != null)
            {
                foreach (var message in list.Messages)
                {
                    results.Add(ToProviderMessage(message));
                }
            }

            next = string.IsNullOrEmpty(list.NextPageUri) ? null : list.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageResource> UpdateMessageAsync(
        string providerMessageSid,
        List<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await SendMessagingAsync(HttpMethod.Post, MessageInstancePath(providerMessageSid), content, cancellationToken);
    }

    private HttpClient MessagingClient() => _httpClientFactory.CreateClient(MessagingClientName);

    private string MessagingPath(string relative)
    {
        return $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/{relative}";
    }

    private string MessageInstancePath(string providerMessageSid)
    {
        return MessagingPath($"Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
    }

    private Uri ResolveMessagingUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        {
            var messagingBase = GetMessagingBaseAddress();
            return new Uri(messagingBase, absolute.PathAndQuery);
        }

        var relative = uri.TrimStart('/');
        return new Uri(GetMessagingBaseAddress(), relative);
    }

    private Uri GetMessagingBaseAddress()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl);
    }

    private async Task<TwilioMessageResource> SendMessagingAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, ResolveMessagingUri(relativePath))
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await MessagingClient().SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Messaging API call failed because the provider could not be reached.");
            throw new TwilioApiException("Messaging API call failed.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Messaging API call failed with HTTP {Status} on {Path}.",
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri?.AbsolutePath);
            throw new TwilioApiException("Messaging API call failed.")
            {
                HttpStatus = (int)response.StatusCode,
                TwilioErrorCode = TryReadErrorCode(payload)
            };
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        if (resource is null)
        {
            throw new TwilioApiException("Messaging API returned an empty body.");
        }

        return resource;
    }

    private async Task<ListMessageResponse> SendMessagingListAsync(
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await MessagingClient().GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Messaging list call failed because the provider could not be reached.");
            throw new TwilioApiException("Messaging API call failed.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Messaging list call failed with HTTP {Status} on {Path}.",
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri?.AbsolutePath);
            throw new TwilioApiException("Messaging API call failed.")
            {
                HttpStatus = (int)response.StatusCode,
                TwilioErrorCode = TryReadErrorCode(payload)
            };
        }

        return JsonSerializer.Deserialize<ListMessageResponse>(payload, JsonOptions)
               ?? new ListMessageResponse();
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageResource resource)
    {
        return new ProviderMessage(
            resource.Sid,
            resource.Status,
            resource.ErrorCode,
            resource.Body,
            resource.From,
            resource.To,
            ParseRfc2822(resource.DateCreated),
            ParseRfc2822(resource.DateSent),
            ParseRfc2822(resource.DateUpdated));
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? TryReadErrorCode(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Provider error bodies are not logged; a missing code is fine.
        }

        return null;
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
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

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }
    }

    private sealed class ListMessageResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
