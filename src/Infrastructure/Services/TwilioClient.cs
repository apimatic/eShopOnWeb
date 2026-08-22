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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioClient : ITwilioMessagingClient, ITwilioLookupClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TwilioSettings _settings;
    private readonly HttpClient _messagingHttp;
    private readonly HttpClient _lookupHttp;
    private readonly bool _ownsClients;

    public TwilioClient(IOptions<TwilioSettings> options)
        : this(options, messagingHttp: null, lookupHttp: null)
    {
    }

    internal TwilioClient(IOptions<TwilioSettings> options, HttpClient? messagingHttp, HttpClient? lookupHttp)
    {
        _settings = options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));

        if (messagingHttp is null)
        {
            _messagingHttp = CreateHttpClient(credentials);
            _ownsClients = true;
        }
        else
        {
            _messagingHttp = messagingHttp;
            EnsureAuth(_messagingHttp, credentials);
        }

        if (lookupHttp is null)
        {
            _lookupHttp = CreateHttpClient(credentials);
            _ownsClients = true;
        }
        else
        {
            _lookupHttp = lookupHttp;
            EnsureAuth(_lookupHttp, credentials);
        }
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://lookups.twilio.com" + path));
        using var response = await _lookupHttp.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new TwilioLookupResult(false, null);
        }

        var payload = await DeserializeAsync<LookupResponse>(response, cancellationToken);
        if (payload is null || !payload.Valid || string.IsNullOrWhiteSpace(payload.PhoneNumber))
        {
            return new TwilioLookupResult(false, payload?.PhoneNumber);
        }

        return new TwilioLookupResult(true, payload.PhoneNumber);
    }

    public async Task<TwilioMessageResult> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        if (sendAt.HasValue)
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _messagingHttp.PostAsync(MessagesCollectionUri(), content, cancellationToken);
        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _messagingHttp.GetAsync(MessageInstanceUri(messageSid), cancellationToken);
        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _messagingHttp.PostAsync(MessageInstanceUri(messageSid), content, cancellationToken);
        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.ASCII.GetBytes("Body=");
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await _messagingHttp.PostAsync(MessageInstanceUri(messageSid), content, cancellationToken);
        var updated = await ReadRequiredMessageAsync(response, cancellationToken);
        if (!string.IsNullOrEmpty(updated.Body))
        {
            updated = await FetchAsync(messageSid, cancellationToken);
        }

        return updated;
    }

    public async Task<IReadOnlyList<TwilioMessageResult>> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var relative = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json"
            + $"?From={Uri.EscapeDataString(_settings.FromNumber)}"
            + $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}"
            + $"&DateSent%3C={Uri.EscapeDataString(toUtc)}"
            + "&PageSize=1000";

        var results = new List<TwilioMessageResult>();
        var next = MessagingUri(relative);

        while (next is not null)
        {
            using var response = await _messagingHttp.GetAsync(next, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await DeserializeAsync<MessageListResponse>(response, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = MapMessage(message);
                    if (mapped is not null)
                    {
                        results.Add(mapped);
                    }
                }
            }

            next = string.IsNullOrWhiteSpace(page?.NextPageUri)
                ? null
                : MessagingUri(page!.NextPageUri!);
        }

        return results;
    }

    public void Dispose()
    {
        if (!_ownsClients)
        {
            return;
        }

        _messagingHttp.Dispose();
        _lookupHttp.Dispose();
    }

    private Uri MessagesCollectionUri()
        => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json");

    private Uri MessageInstanceUri(string messageSid)
        => MessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

    private Uri MessagingUri(string relativePathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? "https://api.twilio.com"
            : _settings.BaseUrl.TrimEnd('/');

        string pathAndQuery;
        if (relativePathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(relativePathAndQuery, UriKind.Absolute, out var absolute))
        {
            pathAndQuery = absolute.PathAndQuery;
        }
        else
        {
            pathAndQuery = relativePathAndQuery;
        }

        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        if (!Uri.TryCreate(baseUrl + pathAndQuery, UriKind.Absolute, out var uri))
        {
            throw new TwilioApiException(500, null);
        }

        return uri;
    }

    private static async Task<TwilioMessageResult> ReadRequiredMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<MessageResponse>(response, cancellationToken);
        var mapped = MapMessage(payload);
        if (mapped is null)
        {
            throw new TwilioApiException((int)response.StatusCode, null);
        }

        return mapped;
    }

    private static TwilioMessageResult? MapMessage(MessageResponse? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Sid) || string.IsNullOrWhiteSpace(payload.Status))
        {
            return null;
        }

        return new TwilioMessageResult(
            payload.Sid,
            payload.Status,
            payload.Body,
            ParseTwilioDate(payload.DateSent),
            ParseTwilioDate(payload.DateCreated),
            payload.ErrorCode);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new TwilioApiException((int)response.StatusCode, await TryReadErrorCodeAsync(response, cancellationToken));
    }

    private static async Task<int?> TryReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static HttpClient CreateHttpClient(string credentials)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        EnsureAuth(client, credentials);
        return client;
    }

    private static void EnsureAuth(HttpClient client, string credentials)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }
}

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? twilioErrorCode)
        : base($"Twilio request failed with HTTP {statusCode}.")
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
    }

    public int StatusCode { get; }
    public int? TwilioErrorCode { get; }
}
