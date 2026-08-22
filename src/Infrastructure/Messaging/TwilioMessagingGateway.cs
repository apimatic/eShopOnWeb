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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingGateway : IMessagingGateway
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioOptions> _options;

    public TwilioMessagingGateway(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<ProviderMessage> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken = default)
    {
        var settings = GetRequiredSettings();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Scheduled messages require Twilio:MessagingServiceSid.");
            }

            fields.Add(new("MessagingServiceSid", settings.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }
        else
        {
            fields.Add(new("From", settings.FromNumber));
            if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            {
                fields.Add(new("MessagingServiceSid", settings.MessagingServiceSid));
            }
        }

        var url = BuildMessagingUrl($"/2010-04-01/Accounts/{settings.AccountSid}/Messages.json");
        using var response = await SendWithRetryAsync(
            () => CreateMessagingRequest(HttpMethod.Post, url, settings, fields),
            cancellationToken);

        var payload = await ReadAsMessageAsync(response, cancellationToken);
        return ToProviderMessage(payload);
    }

    public async Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var settings = GetRequiredSettings();
        var url = BuildMessagingUrl($"/2010-04-01/Accounts/{settings.AccountSid}/Messages/{providerMessageSid}.json");
        using var response = await SendWithRetryAsync(
            () => CreateMessagingRequest(HttpMethod.Get, url, settings, fields: null),
            cancellationToken);

        var payload = await ReadAsMessageAsync(response, cancellationToken);
        return ToProviderMessage(payload);
    }

    public async Task<ProviderMessage> UpdateAsync(string providerMessageSid, MessageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var settings = GetRequiredSettings();
        var fields = new List<KeyValuePair<string, string>>();
        if (request.Body != null)
        {
            fields.Add(new("Body", request.Body));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            fields.Add(new("Status", request.Status));
        }

        var url = BuildMessagingUrl($"/2010-04-01/Accounts/{settings.AccountSid}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json");
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            response?.Dispose();
            response = await SendWithRetryAsync(
                () => CreateMessagingRequest(HttpMethod.Post, url, settings, fields),
                cancellationToken);

            if (response.IsSuccessStatusCode || (int)response.StatusCode != 404 || attempt == 3)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
        }

        using (response)
        {
            var payload = await ReadAsMessageAsync(response!, cancellationToken);
            return ToProviderMessage(payload);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromSenderAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var settings = GetRequiredSettings();
        var messages = new List<ProviderMessage>();
        var path = $"/2010-04-01/Accounts/{settings.AccountSid}/Messages.json?From={Uri.EscapeDataString(fromNumber)}&PageSize=1000&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}";
        var nextUrl = BuildMessagingUrl(path);
        var pages = 0;

        while (!string.IsNullOrEmpty(nextUrl) && pages < 100)
        {
            pages++;
            var url = nextUrl;
            using var response = await SendWithRetryAsync(
                () => CreateMessagingRequest(HttpMethod.Get, url, settings, fields: null),
                cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, json);
            var page = JsonSerializer.Deserialize<TwilioMessageListPayload>(json, JsonOptions)
                ?? new TwilioMessageListPayload();

            if (page.Messages != null)
            {
                foreach (var item in page.Messages)
                {
                    messages.Add(ToProviderMessage(item));
                }
            }

            nextUrl = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : ResolveMessagingUri(page.NextPageUri);
        }

        return messages;
    }

    private TwilioOptions GetRequiredSettings()
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber must be configured.");
        }

        return settings;
    }

    private string MessagingBaseUrl
    {
        get
        {
            var configured = _options.Value.BaseUrl;
            return string.IsNullOrWhiteSpace(configured) ? DefaultMessagingBaseUrl : configured.TrimEnd('/');
        }
    }

    private string BuildMessagingUrl(string pathAndQuery)
    {
        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return MessagingBaseUrl + pathAndQuery;
    }

    private string ResolveMessagingUri(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        {
            return BuildMessagingUrl(absolute.PathAndQuery);
        }

        return BuildMessagingUrl(pathOrUrl);
    }

    private static void ApplyAuth(HttpRequestMessage request, TwilioOptions settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static HttpRequestMessage CreateMessagingRequest(HttpMethod method, string url, TwilioOptions settings, List<KeyValuePair<string, string>>? fields)
    {
        var httpRequest = new HttpRequestMessage(method, url)
        {
            Version = HttpVersion.Version11
        };
        httpRequest.Headers.ExpectContinue = false;
        ApplyAuth(httpRequest, settings);
        if (fields != null)
        {
            var encoded = string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
            var content = new StringContent(encoded, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            httpRequest.Content = content;
        }

        return httpRequest;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            response?.Dispose();
            response = await _httpClient.SendAsync(requestFactory(), cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode != 503)
            {
                return response;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            if (response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delay = retryAfter;
            }
            else if (response.Headers.RetryAfter?.Date is { } retryAt)
            {
                delay = retryAt - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.FromSeconds(1);
                }
            }

            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static async Task<TwilioMessagePayload> ReadAsMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json);
        return JsonSerializer.Deserialize<TwilioMessagePayload>(json, JsonOptions) ?? new TwilioMessagePayload();
    }

    private static void EnsureSuccess(HttpResponseMessage response, string json)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty("code", out var codeEl))
            {
                code = codeEl.ToString();
            }

            if (doc.RootElement.TryGetProperty("message", out var messageEl))
            {
                message = messageEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Body may not be JSON; surface the status only.
        }

        throw new TwilioApiException((int)response.StatusCode, code, message);
    }

    private static ProviderMessage ToProviderMessage(TwilioMessagePayload payload)
    {
        return new ProviderMessage
        {
            Sid = payload.Sid ?? string.Empty,
            Status = payload.Status ?? "unknown",
            ErrorCode = payload.ErrorCode,
            Body = payload.Body,
            DateSent = ParseTwilioDate(payload.DateSent),
            DateCreated = ParseTwilioDate(payload.DateCreated),
            From = payload.From,
            To = payload.To
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioMessageListPayload
    {
        public List<TwilioMessagePayload>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessagePayload
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? Body { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }
}

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, string? providerCode, string? _)
        : base(BuildMessage(statusCode, providerCode))
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public string? ProviderCode { get; }

    private static string BuildMessage(int statusCode, string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return $"Twilio request failed with HTTP {statusCode}.";
        }

        return $"Twilio request failed with HTTP {statusCode} (code {providerCode}).";
    }
}
