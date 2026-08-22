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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ISmsMessagingService
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ProviderSendResult> SendAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["From"] = _settings.FromNumber,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                _logger.LogWarning("A scheduled message was requested but Twilio:MessagingServiceSid is not configured.");
                return new ProviderSendResult { Accepted = false, ErrorStatus = "failed" };
            }

            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(form);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUrl)
        {
            Content = content
        };
        ApplyBasicAuth(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twilio create-message request failed to complete.");
            return new ProviderSendResult { Accepted = false, ErrorStatus = "failed" };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 201)
        {
            var message = DeserializeMessage(body);
            return new ProviderSendResult { Accepted = true, Message = message };
        }

        var error = TryDeserializeError(body);
        _logger.LogWarning(
            "Twilio rejected a create-message request. HTTP {StatusCode}, provider code {ErrorCode}.",
            (int)response.StatusCode,
            error?.Code);

        return new ProviderSendResult
        {
            Accepted = false,
            ErrorCode = error?.Code,
            ErrorStatus = "failed"
        };
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageResourceUrl(messageSid));
        ApplyBasicAuth(httpRequest);

        var response = await SendWithRetryAsync(httpRequest, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return DeserializeMessage(body);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var fromDate = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var firstUrl =
            $"{MessagesCollectionUrl}?From={Uri.EscapeDataString(fromNumber)}" +
            $"&PageSize=1000" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDate)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toDate)}";

        var results = new List<ProviderMessage>();
        var nextUrl = firstUrl;
        var pages = 0;

        while (!string.IsNullOrEmpty(nextUrl) && pages < 100)
        {
            pages++;
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, ResolveMessagingUri(nextUrl));
            ApplyBasicAuth(httpRequest);

            var response = await SendWithRetryAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                       ?? new TwilioMessageListResponse();

            if (page.Messages is not null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(ToProviderMessage(item));
                }
            }

            nextUrl = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    public Task<ProviderMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public Task<ProviderMessage?> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    private async Task<ProviderMessage?> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var content = new FormUrlEncodedContent(form);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageResourceUrl(messageSid))
        {
            Content = content
        };
        ApplyBasicAuth(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await SendWithRetryAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twilio update-message request failed to complete for SID {MessageSid}.", messageSid);
            throw new TwilioUnavailableException("The messaging provider could not be reached.", ex);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserializeError(body);
            _logger.LogWarning(
                "Twilio rejected an update-message request for SID {MessageSid}. HTTP {StatusCode}, provider code {ErrorCode}.",
                messageSid,
                (int)response.StatusCode,
                error?.Code);
            throw new TwilioUnavailableException(
                $"The messaging provider rejected the update (HTTP {(int)response.StatusCode}).");
        }

        return DeserializeMessage(body);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var request = await CloneAsync(template, cancellationToken);
            try
            {
                lastResponse = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Transient failure calling Twilio messaging API (attempt {Attempt}).", attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), cancellationToken);
                continue;
            }

            var status = (int)lastResponse!.StatusCode;
            if (attempt < 2 && (status == 429 || status >= 500))
            {
                lastResponse.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), cancellationToken);
                continue;
            }

            return lastResponse;
        }

        return lastResponse!;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri);
        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (template.Content is not null)
        {
            var bytes = await template.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in template.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) ||
            string.IsNullOrWhiteSpace(_settings.AuthToken) ||
            string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new TwilioUnavailableException("Twilio messaging is not configured.");
        }
    }

    private string MessagingBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return _settings.BaseUrl.TrimEnd('/');
            }

            return DefaultMessagingBaseUrl;
        }
    }

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageResourceUrl(string sid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                var configuredBase = new Uri(MessagingBaseUrl + "/");
                return new Uri(configuredBase, absolute.PathAndQuery.TrimStart('/'));
            }

            return absolute;
        }

        return new Uri(new Uri(MessagingBaseUrl + "/"), nextPageUri.TrimStart('/'));
    }

    private static ProviderMessage DeserializeMessage(string json)
    {
        var dto = JsonSerializer.Deserialize<TwilioMessageDto>(json, JsonOptions) ?? new TwilioMessageDto();
        return ToProviderMessage(dto);
    }

    private static TwilioErrorDto? TryDeserializeError(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto)
    {
        return new ProviderMessage
        {
            Sid = dto.Sid,
            Status = dto.Status,
            ErrorCode = dto.ErrorCode,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            DateCreated = ParseTwilioDate(dto.DateCreated),
            DateSent = ParseTwilioDate(dto.DateSent),
            DateUpdated = ParseTwilioDate(dto.DateUpdated)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
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

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? DateCreated { get; set; }
        public string? DateSent { get; set; }
        public string? DateUpdated { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorDto
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public int Status { get; set; }
    }
}
