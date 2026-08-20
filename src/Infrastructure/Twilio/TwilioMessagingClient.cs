using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultHost = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        ConfigureClient(_httpClient, _settings);
    }

    public string FromNumber => _settings.FromNumber;

    internal static string ResolveBaseUrl(TwilioSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultHost : settings.BaseUrl.Trim();
    }

    internal static void ConfigureClient(HttpClient httpClient, TwilioSettings settings)
    {
        var baseUrl = ResolveBaseUrl(settings);
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        TwilioLookupClient.ApplyBasicAuth(httpClient, settings);
    }

    public async Task<ProviderMessage> SendAsync(
        string toE164,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("A Messaging Service SID is required to schedule a message.");
            }

            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        var path = MessagesCollectionPath();
        using var response = await TwilioHttpRetry.SendAsync(
            () => _httpClient.PostAsync(path, new FormUrlEncodedContent(fields), cancellationToken),
            allowRetryOnSuccessPath: false,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode != 201 && !response.IsSuccessStatusCode)
        {
            throw TwilioHttpRetry.ToApiException(response, payload);
        }

        return MapMessage(payload);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var path = MessageInstancePath(messageSid);
        using var response = await TwilioHttpRetry.SendAsync(
            () => _httpClient.GetAsync(path, cancellationToken),
            allowRetryOnSuccessPath: true,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioHttpRetry.ToApiException(response, payload);
        }

        return MapMessage(payload);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&PageSize=1000");
        query.Append("&").Append(Uri.EscapeDataString("DateSent>")).Append('=')
            .Append(Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        query.Append("&").Append(Uri.EscapeDataString("DateSent<")).Append('=')
            .Append(Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));

        var results = new List<ProviderMessage>();
        string? next = MessagesCollectionPath() + "?" + query;

        while (!string.IsNullOrEmpty(next))
        {
            var requestUri = ToMessagingRequestUri(next);
            using var response = await TwilioHttpRetry.SendAsync(
                () => _httpClient.GetAsync(requestUri, cancellationToken),
                allowRetryOnSuccessPath: true,
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioHttpRetry.ToApiException(response, payload);
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                       ?? new TwilioMessageListResponse();

            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(MapResource(message));
                }
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        return UpdateAsync(messageSid, fields, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        return UpdateAsync(messageSid, fields, cancellationToken);
    }

    private async Task<ProviderMessage> UpdateAsync(
        string messageSid,
        List<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = MessageInstancePath(messageSid);
        using var response = await TwilioHttpRetry.SendAsync(
            () => _httpClient.PostAsync(path, new FormUrlEncodedContent(fields), cancellationToken),
            allowRetryOnSuccessPath: true,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioHttpRetry.ToApiException(response, payload);
        }

        return MapMessage(payload);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri ToMessagingRequestUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            var relative = absolute.PathAndQuery.TrimStart('/');
            return new Uri(_httpClient.BaseAddress!, relative);
        }

        return new Uri(_httpClient.BaseAddress!, nextPageUri.TrimStart('/'));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid)
            || string.IsNullOrWhiteSpace(_settings.AuthToken)
            || string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio messaging is not configured.");
        }
    }

    private static ProviderMessage MapMessage(string payload)
    {
        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions)
                       ?? throw new InvalidOperationException("The provider returned an empty message resource.");
        return MapResource(resource);
    }

    private static ProviderMessage MapResource(TwilioMessageResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Sid))
        {
            throw new InvalidOperationException("The provider message did not include a SID.");
        }

        return new ProviderMessage(
            resource.Sid,
            resource.Status ?? string.Empty,
            resource.ErrorCode,
            resource.Body,
            ParseRfc2822(resource.DateSent),
            ParseRfc2822(resource.DateCreated),
            resource.From);
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
