using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

public class TwilioSmsGateway : ISmsNotificationGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private static readonly TimeSpan RetryBase = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryCap = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string FromNumber => _options.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}";
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, url),
            retryCreate: true,
            cancellationToken);

        var payload = await ReadAs<TwilioLookupResponse>(response, cancellationToken);
        if (payload is null)
        {
            return new PhoneNumberLookupResult(false, null, null, new[] { "LOOKUP_FAILED" });
        }

        var errors = payload.ValidationErrors ?? Array.Empty<string>();
        return new PhoneNumberLookupResult(payload.Valid, payload.PhoneNumber, payload.NationalFormat, errors);
    }

    public async Task<SmsSendAttempt> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<(string Key, string Value)>
        {
            ("To", request.To),
            ("Body", request.Body)
        };

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                _logger.LogWarning("Scheduled SMS skipped because Twilio:MessagingServiceSid is not configured.");
                return new SmsSendAttempt(false, null, null);
            }

            fields.Add(("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(("ScheduleType", "fixed"));
            fields.Add(("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
            if (!string.IsNullOrWhiteSpace(_options.FromNumber))
            {
                fields.Add(("From", _options.FromNumber));
            }
        }
        else
        {
            fields.Add(("From", _options.FromNumber));
        }

        using var response = await SendWithRetryAsync(
            () =>
            {
                var httpRequest = CreateRequest(HttpMethod.Post, MessagesCollectionUrl);
                httpRequest.Content = CreateForm(fields);
                return httpRequest;
            },
            retryCreate: false,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Created)
        {
            var created = await ReadAs<TwilioMessageResource>(response, cancellationToken);
            return new SmsSendAttempt(true, ToProviderMessage(created), created?.ErrorCode);
        }

        var error = await ReadAs<TwilioErrorBody>(response, cancellationToken);
        _logger.LogWarning(
            "Create Message was rejected with HTTP {Status} provider code {Code}.",
            (int)response.StatusCode,
            error?.Code);
        return new SmsSendAttempt(false, null, error?.Code);
    }

    public async Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, MessageInstanceUrl(providerMessageSid)),
            retryCreate: true,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch Message returned HTTP {Status}.", (int)response.StatusCode);
            return null;
        }

        return ToProviderMessage(await ReadAs<TwilioMessageResource>(response, cancellationToken));
    }

    public Task<ProviderMessage?> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(providerMessageSid, body: null, status: "canceled", cancellationToken);

    public Task<ProviderMessage?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(providerMessageSid, body: string.Empty, status: null, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var fromDate = DateOnly.FromDateTime(from.UtcDateTime);
        var toDateExclusive = DateOnly.FromDateTime(to.UtcDateTime).AddDays(1);
        var url =
            $"{MessagesCollectionUrl}?From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDate.ToString("yyyy-MM-dd"))}" +
            $"&DateSent%3C={Uri.EscapeDataString(toDateExclusive.ToString("yyyy-MM-dd"))}" +
            "&PageSize=1000";

        while (!string.IsNullOrEmpty(url))
        {
            var pageUrl = url;
            using var response = await SendWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, pageUrl),
                retryCreate: true,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List Message returned HTTP {Status}.", (int)response.StatusCode);
                break;
            }

            var page = await ReadAs<TwilioMessageListResponse>(response, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = ToProviderMessage(message);
                    if (mapped is not null)
                    {
                        results.Add(mapped);
                    }
                }
            }

            url = ResolveNextPageUrl(page?.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage?> UpdateMessageAsync(
        string providerMessageSid,
        string? body,
        string? status,
        CancellationToken cancellationToken)
    {
        var fields = new List<(string Key, string Value)>();
        if (body is not null)
        {
            fields.Add(("Body", body));
        }

        if (status is not null)
        {
            fields.Add(("Status", status));
        }

        using var response = await SendWithRetryAsync(
            () =>
            {
                var httpRequest = CreateRequest(HttpMethod.Post, MessageInstanceUrl(providerMessageSid));
                httpRequest.Content = CreateForm(fields);
                return httpRequest;
            },
            retryCreate: true,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Update Message returned HTTP {Status}.", (int)response.StatusCode);
            return null;
        }

        return ToProviderMessage(await ReadAs<TwilioMessageResource>(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> createRequest,
        bool retryCreate,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            lastResponse?.Dispose();
            var request = createRequest();
            lastResponse = await _httpClient.SendAsync(request, cancellationToken);

            if (!IsRetryable(lastResponse, retryCreate) || attempt == MaxAttempts - 1)
            {
                return lastResponse;
            }

            await DelayForRetryAsync(lastResponse, attempt, cancellationToken);
        }

        return lastResponse!;
    }

    private static bool IsRetryable(HttpResponseMessage response, bool retryCreate)
    {
        var status = (int)response.StatusCode;
        if (status == 429 || status == 503)
        {
            return true;
        }

        if (status >= 500 && retryCreate)
        {
            return true;
        }

        return false;
    }

    private static async Task DelayForRetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
        {
            await Task.Delay(retryAfter, cancellationToken);
            return;
        }

        var windowMs = Math.Min(RetryCap.TotalMilliseconds, RetryBase.TotalMilliseconds * Math.Pow(2, attempt));
        var jitter = Random.Shared.NextDouble() * windowMs;
        await Task.Delay(TimeSpan.FromMilliseconds(jitter), cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static ByteArrayContent CreateForm(IEnumerable<(string Key, string Value)> fields)
    {
        var pairs = new List<string>();
        foreach (var (key, value) in fields)
        {
            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(string.Join("&", pairs)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    private static async Task<T?> ReadAs<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static ProviderMessage? ToProviderMessage(TwilioMessageResource? resource)
    {
        if (resource is null)
        {
            return null;
        }

        return new ProviderMessage
        {
            Sid = resource.Sid,
            Status = resource.Status,
            ErrorCode = resource.ErrorCode,
            Body = resource.Body,
            From = resource.From,
            DateSent = resource.DateSent,
            DateCreated = resource.DateCreated
        };
    }

    private string MessagingRoot
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                return DefaultMessagingBaseUrl;
            }

            return _options.BaseUrl.TrimEnd('/');
        }
    }

    private string MessagesCollectionUrl =>
        $"{MessagingRoot}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstanceUrl(string sid) =>
        $"{MessagingRoot}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string? ResolveNextPageUrl(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                return nextPageUri;
            }

            var built = new Uri(nextPageUri);
            return MessagingRoot + built.PathAndQuery;
        }

        if (!nextPageUri.StartsWith('/'))
        {
            nextPageUri = "/" + nextPageUri;
        }

        return MessagingRoot + nextPageUri;
    }
}
