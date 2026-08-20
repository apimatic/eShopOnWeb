using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioGateway : ITwilioGateway
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupsClientName = "TwilioLookups";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioOptions> _options;
    private readonly ILogger<TwilioGateway> _logger;

    public TwilioGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        ILogger<TwilioGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = CreateLookupsClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(client.BaseAddress!, $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}"));
        ApplyAuth(request);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new PhoneLookupResult(false, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                await LogTwilioFailureAsync(response, "lookup", cancellationToken);
                return new PhoneLookupResult(false, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, JsonOptions, cancellationToken);
            if (payload is { Valid: true } && !string.IsNullOrWhiteSpace(payload.PhoneNumber))
            {
                return new PhoneLookupResult(true, payload.PhoneNumber);
            }

            return new PhoneLookupResult(false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Phone lookup failed: {Message}", ex.Message);
            return new PhoneLookupResult(false, null);
        }
    }

    public async Task<TwilioMessageSnapshot?> SendSmsAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(options.MessagingServiceSid))
            {
                _logger.LogWarning("Cannot schedule an SMS because MessagingServiceSid is not configured");
                return null;
            }

            form["MessagingServiceSid"] = options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (!string.IsNullOrWhiteSpace(options.FromNumber))
            {
                form["From"] = options.FromNumber;
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.FromNumber))
        {
            form["From"] = options.FromNumber;
        }

        return await SendMessageRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Post, MessagingUri(MessagesCollectionPath()))
            {
                Content = CreateFormContent(form)
            },
            "create",
            maxAttempts: 1,
            cancellationToken);
    }

    public Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => SendMessageRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Get, MessagingUri(MessageInstancePath(messageSid))),
            "fetch",
            maxAttempts: 1,
            cancellationToken);

    public Task<TwilioMessageSnapshot?> UpdateMessageAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>();
        if (body != null)
        {
            form["Body"] = body;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            form["Status"] = status;
        }

        return SendMessageRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Post, MessagingUri(MessageInstancePath(messageSid)))
            {
                Content = CreateFormContent(form)
            },
            "update",
            maxAttempts: 4,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var results = new List<TwilioMessageSnapshot>();
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(options.FromNumber)}&PageSize=1000&DateSent%3E={Uri.EscapeDataString(fromIso)}&DateSent%3C={Uri.EscapeDataString(toIso)}";

        while (!string.IsNullOrEmpty(path))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MessagingUri(path));
            ApplyAuth(request);

            var client = CreateMessagingClient();
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await LogTwilioFailureAsync(response, "list", cancellationToken);
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessageListResponse>(stream, JsonOptions, cancellationToken);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    var snapshot = ToSnapshot(message);
                    if (snapshot != null)
                    {
                        results.Add(snapshot);
                    }
                }
            }

            path = ResolveNextPagePath(page?.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot?> SendMessageRequestAsync(
        Func<HttpRequestMessage> createRequest,
        string operation,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = createRequest();
                ApplyAuth(request);
                var client = CreateMessagingClient();
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound && attempt < maxAttempts)
                {
                    await Task.Delay(400 * attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    await LogTwilioFailureAsync(response, operation, cancellationToken);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<MessageResource>(stream, JsonOptions, cancellationToken);
                return ToSnapshot(payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Twilio {Operation} failed: {Message}", operation, ex.Message);
                if (attempt == maxAttempts)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private HttpClient CreateMessagingClient()
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        client.BaseAddress = new Uri(GetMessagingBaseUrl());
        return client;
    }

    private HttpClient CreateLookupsClient()
    {
        var client = _httpClientFactory.CreateClient(LookupsClientName);
        client.BaseAddress = new Uri("https://lookups.twilio.com/");
        return client;
    }

    private string GetMessagingBaseUrl()
        => string.IsNullOrWhiteSpace(_options.Value.BaseUrl)
            ? "https://api.twilio.com/"
            : _options.Value.BaseUrl.TrimEnd('/') + "/";

    private Uri MessagingUri(string relativePathAndQuery)
        => new(new Uri(GetMessagingBaseUrl(), UriKind.Absolute), relativePathAndQuery.TrimStart('/'));

    private void ApplyAuth(HttpRequestMessage request)
    {
        var options = _options.Value;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static FormUrlEncodedContent CreateFormContent(Dictionary<string, string> form)
    {
        var content = new FormUrlEncodedContent(form);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_options.Value.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_options.Value.AccountSid}/Messages/{sid}.json";

    private static string? ResolveNextPagePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private async Task LogTwilioFailureAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var code = 0;
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<TwilioErrorBody>(json, JsonOptions);
            code = error?.Code ?? 0;
        }
        catch
        {
            // Never log the raw provider body; it can include destination numbers.
        }

        _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode} (provider code {ProviderCode})",
            operation, (int)response.StatusCode, code);
    }

    private static TwilioMessageSnapshot? ToSnapshot(MessageResource? message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.Sid))
        {
            return null;
        }

        return new TwilioMessageSnapshot(
            message.Sid,
            message.Status ?? "unknown",
            message.Body,
            message.ErrorCode,
            message.ErrorMessage,
            ParseTwilioDate(message.DateSent),
            ParseTwilioDate(message.DateCreated));
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResource>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorBody
    {
        public int? Code { get; set; }
    }
}
