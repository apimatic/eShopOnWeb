using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioSmsProvider : ISmsProvider
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public string MessagingBaseUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return DefaultMessagingBaseUrl;
            }

            return _settings.BaseUrl.TrimEnd('/');
        }
    }

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(rawNumber.Trim());
        var url = $"/v2/PhoneNumbers/{encoded}?Fields={Uri.EscapeDataString("line_type_intelligence")}";
        using var response = await SendLookupAsync(HttpMethod.Get, url, content: null, cancellationToken);
        var payload = await ReadJsonAsync<LookupResponse>(response, cancellationToken);

        if (payload == null)
        {
            return new PhoneLookupResult { Valid = false, ValidationErrors = new[] { "LOOKUP_FAILED" } };
        }

        return new PhoneLookupResult
        {
            Valid = payload.Valid,
            ValidationErrors = payload.ValidationErrors ?? new List<string>(),
            CanonicalPhoneNumber = payload.PhoneNumber,
            NationalFormat = payload.NationalFormat,
            CountryCode = payload.CountryCode,
            LineType = payload.LineTypeIntelligence?.Type,
            LineTypeErrorCode = payload.LineTypeIntelligence?.ErrorCode
        };
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            form.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            form.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        var path = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var response = await SendMessagingAsync(HttpMethod.Post, path, form, isCreateMessage: true, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Created || response.IsSuccessStatusCode)
        {
            var message = JsonSerializer.Deserialize<TwilioMessageDto>(body, JsonOptions);
            return new SmsSendResult
            {
                Accepted = true,
                MessageSid = message?.Sid,
                Status = message?.Status,
                ErrorCode = message?.ErrorCode
            };
        }

        var error = TryReadError(body);
        _logger.LogWarning(
            "Create Message was rejected with HTTP {Status} code {Code}.",
            (int)response.StatusCode,
            error?.Code ?? 0);
        return new SmsSendResult
        {
            Accepted = false,
            ErrorCode = error?.Code,
            FailureReason = $"Provider returned HTTP {(int)response.StatusCode} (code {error?.Code})."
        };
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await SendMessagingAsync(HttpMethod.Get, path, form: null, isCreateMessage: false, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return dto == null ? null : ToSnapshot(dto);
    }

    public async Task<SmsMessageSnapshot?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        return await UpdateMessageAsync(messageSid, form, cancellationToken);
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };
        return await UpdateMessageAsync(messageSid, form, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var path = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json"
            + $"?From={Uri.EscapeDataString(fromNumber)}"
            + $"&PageSize=1000"
            + $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}"
            + $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}";

        string? next = path;
        while (!string.IsNullOrEmpty(next))
        {
            using var response = await SendMessagingAsync(HttpMethod.Get, next, form: null, isCreateMessage: false, cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = await ReadJsonAsync<TwilioMessageListDto>(response, cancellationToken);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = string.IsNullOrEmpty(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return results;
    }

    private async Task<SmsMessageSnapshot?> UpdateMessageAsync(
        string messageSid,
        List<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        var path = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await SendMessagingAsync(HttpMethod.Post, path, form, isCreateMessage: false, cancellationToken);
        response.EnsureSuccessStatusCode();
        var dto = await ReadJsonAsync<TwilioMessageDto>(response, cancellationToken);
        return dto == null ? null : ToSnapshot(dto);
    }

    private async Task<HttpResponseMessage> SendMessagingAsync(
        HttpMethod method,
        string pathOrUri,
        List<KeyValuePair<string, string>>? form,
        bool isCreateMessage,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        return await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(method, ResolveMessagingUri(pathOrUri));
                ApplyBasicAuth(request);
                if (form != null)
                {
                    request.Content = EncodeForm(form);
                }

                return request;
            },
            isCreateMessage,
            client,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendLookupAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(LookupClientName);
        return await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(method, path);
                ApplyBasicAuth(request);
                if (content != null)
                {
                    request.Content = content;
                }

                return request;
            },
            isCreateMessage: false,
            client,
            cancellationToken);
    }

    private Uri ResolveMessagingUri(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            var configured = new Uri(MessagingBaseUrl + "/");
            return new Uri(configured, absolute.PathAndQuery);
        }

        return new Uri(MessagingBaseUrl + pathOrUri);
    }

    private static HttpContent EncodeForm(IEnumerable<KeyValuePair<string, string>> form)
    {
        var body = string.Join("&", form.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
        return new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        bool isCreateMessage,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        const int maxTries = 5;
        var delayBase = TimeSpan.FromMilliseconds(500);
        var cap = TimeSpan.FromSeconds(30);

        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < maxTries; attempt++)
        {
            last?.Dispose();
            using var request = requestFactory();
            last = await client.SendAsync(request, cancellationToken);

            var status = (int)last.StatusCode;
            var retryable = status == 429 || status == 503 || (status == 500 && !isCreateMessage);
            if (!retryable || attempt == maxTries - 1)
            {
                return last;
            }

            var wait = ReadRetryAfter(last) ?? FullJitter(delayBase, cap, attempt);
            _logger.LogInformation("Retrying provider request after {Delay} (HTTP {Status}).", wait, status);
            await Task.Delay(wait, cancellationToken);
        }

        return last!;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var value in values)
            {
                if (double.TryParse(value, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }

        return null;
    }

    private static TimeSpan FullJitter(TimeSpan delayBase, TimeSpan cap, int attempt)
    {
        var windowMs = Math.Min(cap.TotalMilliseconds, delayBase.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * windowMs);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static TwilioErrorDto? TryReadError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageDto dto)
    {
        return new SmsMessageSnapshot
        {
            Sid = dto.Sid,
            Status = dto.Status,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            ErrorCode = dto.ErrorCode,
            DateSent = dto.DateSent,
            DateCreated = dto.DateCreated,
            Direction = dto.Direction,
            MessagingServiceSid = dto.MessagingServiceSid
        };
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public List<string>? ValidationErrors { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string? CountryCode { get; set; }
        public LineTypeIntelligenceDto? LineTypeIntelligence { get; set; }
    }

    private sealed class LineTypeIntelligenceDto
    {
        public string? Type { get; set; }
        public int? ErrorCode { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
        public string? Direction { get; set; }
        public string? MessagingServiceSid { get; set; }
    }

    private sealed class TwilioMessageListDto
    {
        public List<TwilioMessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public int Status { get; set; }
    }
}
