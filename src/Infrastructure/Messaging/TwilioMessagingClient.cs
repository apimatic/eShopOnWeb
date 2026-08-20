using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ISmsGateway
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _settings = settings.Value;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        if (request.SendAt.HasValue)
        {
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("o");
            if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
            }
        }

        return await PostMessageAsync(fields, cancellationToken);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var request = CreateRequest(HttpMethod.Get, MessagingUri(path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio FetchMessage returned HTTP {StatusCode} for {Sid}.",
                (int)response.StatusCode,
                providerMessageSid);
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        return resource is null ? null : ToSnapshot(resource);
    }

    public async Task<SmsSendResult> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        };
        return await PostMessageInstanceAsync(providerMessageSid, fields, cancellationToken);
    }

    public async Task<SmsSendResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        };
        return await PostMessageInstanceAsync(providerMessageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromNumber = Uri.EscapeDataString(_settings.FromNumber);
        var fromValue = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var toValue = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var pathAndQuery =
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json" +
            $"?From={fromNumber}&DateSent%3E={fromValue}&DateSent%3C={toValue}&PageSize=1000";

        while (!string.IsNullOrEmpty(pathAndQuery))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(pathAndQuery));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Twilio ListMessage returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                break;
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            pathAndQuery = NormalizeNextPage(page?.NextPageUri);
        }

        return results;
    }

    private async Task<SmsSendResult> PostMessageAsync(
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
        using var request = CreateRequest(HttpMethod.Post, MessagingUri(path));
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
            if (resource is null || string.IsNullOrEmpty(resource.Sid))
            {
                return new SmsSendResult(false, null, "failed", null, "The provider returned an empty message resource.");
            }

            return new SmsSendResult(true, resource.Sid, resource.Status, resource.ErrorCode, resource.ErrorMessage);
        }

        var error = TryReadError(payload);
        _logger.LogWarning(
            "Twilio CreateMessage returned HTTP {StatusCode} code {ErrorCode}.",
            (int)response.StatusCode,
            error?.Code);
        return new SmsSendResult(false, null, "failed", error?.Code, PiiRedactor.Redact(error?.Message));
    }

    private async Task<SmsSendResult> PostMessageInstanceAsync(
        string providerMessageSid,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var request = CreateRequest(HttpMethod.Post, MessagingUri(path));
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
            return new SmsSendResult(
                true,
                resource?.Sid ?? providerMessageSid,
                resource?.Status,
                resource?.ErrorCode,
                resource?.ErrorMessage);
        }

        var error = TryReadError(payload);
        _logger.LogWarning(
            "Twilio UpdateMessage returned HTTP {StatusCode} code {ErrorCode} for {Sid}.",
            (int)response.StatusCode,
            error?.Code,
            providerMessageSid);
        return new SmsSendResult(false, providerMessageSid, "failed", error?.Code, PiiRedactor.Redact(error?.Message));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Uri MessagingUri(string pathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return new Uri(baseUrl + pathAndQuery);
    }

    private static string? NormalizeNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return nextPageUri;
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new SmsMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status,
            resource.ErrorCode,
            resource.ErrorMessage,
            resource.Body,
            resource.From,
            resource.DateSent,
            resource.DateCreated);
    }

    private static TwilioErrorResponse? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
