using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioClient : ISmsGateway, IPhoneNumberLookup
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";

    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TwilioSettings> _options;
    private readonly ILogger<TwilioClient> _logger;

    public TwilioClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TwilioSettings> options,
        ILogger<TwilioClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var settings = _options.CurrentValue;
            return !string.IsNullOrWhiteSpace(settings.AccountSid)
                && !string.IsNullOrWhiteSpace(settings.AuthToken)
                && !string.IsNullOrWhiteSpace(settings.FromNumber);
        }
    }

    public string FromNumber => _options.CurrentValue.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var pathNumber = Uri.EscapeDataString(phoneNumber.Trim());
        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{pathNumber}?Fields=line_type_intelligence";

        using var response = await SendWithRetryAsync(
            LookupClientName,
            () =>
            {
                var copy = new HttpRequestMessage(HttpMethod.Get, uri);
                ApplyBasicAuth(copy, settings);
                return copy;
            },
            retryServerErrors: true,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var basicUri = $"{LookupBaseUrl}/v2/PhoneNumbers/{pathNumber}";
            using var basicResponse = await SendWithRetryAsync(
                LookupClientName,
                () =>
                {
                    var copy = new HttpRequestMessage(HttpMethod.Get, basicUri);
                    ApplyBasicAuth(copy, settings);
                    return copy;
                },
                retryServerErrors: true,
                cancellationToken);

            if (!basicResponse.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(basicResponse, cancellationToken);
                throw new InvalidOperationException($"Phone number lookup failed with status {(int)basicResponse.StatusCode} ({error?.Code}).");
            }

            response.Dispose();
            var basicPayload = await basicResponse.Content.ReadFromJsonAsync<TwilioLookupResponse>(JsonOptions, cancellationToken)
                ?? new TwilioLookupResponse();
            return new PhoneNumberLookupResult(
                basicPayload.Valid,
                basicPayload.PhoneNumber,
                basicPayload.NationalFormat,
                basicPayload.CountryCode,
                basicPayload.LineTypeIntelligence?.Type,
                basicPayload.LineTypeIntelligence?.ErrorCode,
                basicPayload.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(JsonOptions, cancellationToken)
            ?? new TwilioLookupResponse();

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.CountryCode,
            payload.LineTypeIntelligence?.Type,
            payload.LineTypeIntelligence?.ErrorCode,
            payload.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = settings.FromNumber,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
        {
            form["MessagingServiceSid"] = settings.MessagingServiceSid;
        }

        if (sendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            {
                _logger.LogWarning("A follow-up cannot be scheduled because Twilio:MessagingServiceSid is not configured.");
                return new SmsSendResult(false, null, "failed", null, "MessagingServiceSid is required to schedule a message.");
            }

            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var uri = MessagesCollectionUri(settings);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new FormUrlEncodedContent(form)
                };
                ApplyBasicAuth(request, settings);
                return request;
            },
            retryServerErrors: false,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Created || response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
            return new SmsSendResult(true, created?.Sid, created?.Status, created?.ErrorCode, created?.ErrorMessage);
        }

        var error = await ReadErrorAsync(response, cancellationToken);
        _logger.LogWarning(
            "Create Message was rejected with HTTP {Status} provider code {Code}.",
            (int)response.StatusCode,
            error?.Code);
        return new SmsSendResult(false, null, "failed", error?.Code, null);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var uri = MessageInstanceUri(settings, providerSid);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                ApplyBasicAuth(request, settings);
                return request;
            },
            retryServerErrors: true,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        return resource is null ? null : ToSnapshot(resource);
    }

    public Task<SmsMessageSnapshot?> CancelAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(providerSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(providerSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var results = new List<SmsMessageSnapshot>();
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        var query = new List<string>
        {
            $"From={Uri.EscapeDataString(settings.FromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(fromUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            $"DateSent%3C={Uri.EscapeDataString(toUtc.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            "PageSize=1000"
        };

        var next = MessagesCollectionUri(settings) + "?" + string.Join("&", query);

        while (!string.IsNullOrWhiteSpace(next))
        {
            var pageUri = next;
            using var response = await SendWithRetryAsync(
                MessagingClientName,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
                    ApplyBasicAuth(request, settings);
                    return request;
                },
                retryServerErrors: true,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResponse>(JsonOptions, cancellationToken)
                ?? new TwilioMessageListResponse();

            foreach (var message in page.Messages)
            {
                var snapshot = ToSnapshot(message);
                if (IsInRange(snapshot, fromUtc, toUtc))
                {
                    results.Add(snapshot);
                }
            }

            next = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : ResolveMessagingUri(settings, page.NextPageUri);
        }

        return results;
    }

    private async Task<SmsMessageSnapshot?> UpdateMessageAsync(
        string providerSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var settings = RequireSettings();
        var uri = MessageInstanceUri(settings, providerSid);
        using var response = await SendWithRetryAsync(
            MessagingClientName,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new FormUrlEncodedContent(form)
                };
                ApplyBasicAuth(request, settings);
                return request;
            },
            retryServerErrors: true,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(JsonOptions, cancellationToken);
        return resource is null ? null : ToSnapshot(resource);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string clientName,
        Func<HttpRequestMessage> createRequest,
        bool retryServerErrors,
        CancellationToken cancellationToken)
    {
        const int maxTries = 5;
        var baseDelay = TimeSpan.FromMilliseconds(500);
        var cap = TimeSpan.FromSeconds(30);
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < maxTries; attempt++)
        {
            lastResponse?.Dispose();
            var client = _httpClientFactory.CreateClient(clientName);
            lastResponse = await client.SendAsync(createRequest(), cancellationToken);

            if (!ShouldRetry(lastResponse, retryServerErrors) || attempt == maxTries - 1)
            {
                return lastResponse;
            }

            var delay = DelayFor(lastResponse, attempt, baseDelay, cap);
            _logger.LogWarning("Retrying a provider request after HTTP {Status}; attempt {Attempt}.", (int)lastResponse.StatusCode, attempt + 1);
            await Task.Delay(delay, cancellationToken);
        }

        return lastResponse!;
    }

    private static bool ShouldRetry(HttpResponseMessage response, bool retryServerErrors)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        return retryServerErrors && response.StatusCode == HttpStatusCode.InternalServerError;
    }

    private static TimeSpan DelayFor(HttpResponseMessage response, int attempt, TimeSpan baseDelay, TimeSpan cap)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            return retryAfter;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var until = retryAt - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        var window = TimeSpan.FromMilliseconds(Math.Min(cap.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, attempt)));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * window.TotalMilliseconds);
    }

    private static void ApplyBasicAuth(HttpRequestMessage request, TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private TwilioSettings RequireSettings()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Twilio messaging is not configured.");
        }

        return _options.CurrentValue;
    }

    private static string MessagingBase(TwilioSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl.TrimEnd('/');
    }

    private static string MessagesCollectionUri(TwilioSettings settings)
    {
        return $"{MessagingBase(settings)}/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";
    }

    private static string MessageInstanceUri(TwilioSettings settings, string sid)
    {
        return $"{MessagingBase(settings)}/2010-04-01/Accounts/{settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";
    }

    private static string ResolveMessagingUri(TwilioSettings settings, string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return absolute.ToString();
            }

            return $"{MessagingBase(settings)}{absolute.PathAndQuery}";
        }

        var relative = nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri;
        return $"{MessagingBase(settings)}{relative}";
    }

    private static async Task<TwilioErrorBody?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwilioErrorBody>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new SmsMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status,
            resource.ErrorCode,
            resource.Body,
            resource.From,
            resource.To,
            ParseRfc2822(resource.DateCreated),
            ParseRfc2822(resource.DateSent));
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

    private static bool IsInRange(SmsMessageSnapshot snapshot, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = snapshot.DateSent ?? snapshot.DateCreated;
        if (stamp is null)
        {
            return true;
        }

        return stamp >= from && stamp <= to;
    }
}
