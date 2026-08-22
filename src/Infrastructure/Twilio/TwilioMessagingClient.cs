using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        // Typed HttpClient instances can carry a BaseAddress from the factory. Messaging
        // calls use absolute URIs (and optional Twilio:BaseUrl); a BaseAddress would
        // combine with those URIs and miss the Message resource.
        _httpClient.BaseAddress = null;
        _settings = options.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default) =>
        CreateMessageAsync(to, body, sendAt: null, cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default) =>
        CreateMessageAsync(to, body, sendAt, cancellationToken);

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, MessagePath(messageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, "fetch", cancellationToken);
    }

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendFormAsync(MessagePath(messageSid), "Status=canceled", "cancel", cancellationToken);

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default) =>
        SendFormAsync(MessagePath(messageSid), "Body=", "redact", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var sender = _settings.FromNumber;
        var results = new List<ProviderMessage>();
        var path = MessagesCollectionPath()
                   + "?From=" + Uri.EscapeDataString(sender)
                   + "&DateSent%3E=" + Uri.EscapeDataString(ToIso8601(from))
                   + "&DateSent%3C=" + Uri.EscapeDataString(ToIso8601(to))
                   + "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = CreateRequest(HttpMethod.Get, path);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderException("list", payload, (int)response.StatusCode);
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, TwilioHttp.JsonOptions);
            if (page?.Messages is not null)
            {
                foreach (var resource in page.Messages)
                {
                    var mapped = Map(resource);
                    if (mapped is not null)
                    {
                        results.Add(mapped);
                    }
                }
            }

            path = ToRelativeMessagingPath(page?.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", ToIso8601(sendAt.Value)));
        }

        using var request = CreateRequest(HttpMethod.Post, MessagesCollectionPath());
        request.Content = CreateFormContent(EncodeForm(fields));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, sendAt.HasValue ? "schedule" : "send", cancellationToken);
    }

    private async Task<ProviderMessage> SendFormAsync(
        string relativePath,
        string formBody,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativePath);
        request.Content = CreateFormContent(formBody);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadMessageAsync(response, operation, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, ToAbsoluteUri(relativePath));
        request.Headers.Authorization = TwilioHttp.CreateAuthHeader(_settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.ExpectContinue = false;
        request.Version = new Version(1, 1);
        return request;
    }

    private Uri ToAbsoluteUri(string relativePath)
    {
        var root = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? "https://api.twilio.com"
            : _settings.BaseUrl.TrimEnd('/');
        return new Uri($"{root}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static StringContent CreateFormContent(string formBody)
    {
        var content = new StringContent(formBody, Encoding.ASCII, "application/x-www-form-urlencoded");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");
        return content;
    }

    private static string EncodeForm(IEnumerable<KeyValuePair<string, string>> fields)
    {
        return string.Join("&", fields.Select(field =>
            Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value ?? string.Empty)));
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(operation, payload, (int)response.StatusCode);
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioHttp.JsonOptions);
        var mapped = Map(resource);
        if (mapped is null)
        {
            throw new InvalidOperationException($"Twilio {operation} returned a message without a SID.");
        }

        return mapped;
    }

    private Exception CreateProviderException(string operation, string payload, int statusCode)
    {
        string? message = null;
        int? twilioCode = null;
        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, TwilioHttp.JsonOptions);
            message = error?.Message;
            twilioCode = error?.Code;
        }
        catch (JsonException)
        {
        }

        var sanitized = TwilioLogSanitizer.Redact(message);
        _logger.LogWarning(
            "Twilio {Operation} failed with HTTP {StatusCode} and provider code {TwilioCode}: {Detail}",
            operation,
            statusCode,
            twilioCode,
            sanitized);

        return new InvalidOperationException(
            $"Twilio {operation} failed with HTTP {statusCode}. {sanitized}".Trim());
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessagePath(string messageSid) =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static string ToIso8601(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? ToRelativeMessagingPath(string? nextPageUri)
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

    private static ProviderMessage? Map(TwilioMessageResource? resource)
    {
        if (resource is null || string.IsNullOrWhiteSpace(resource.Sid))
        {
            return null;
        }

        return new ProviderMessage
        {
            Sid = resource.Sid,
            Status = resource.Status ?? string.Empty,
            Body = resource.Body,
            ErrorCode = resource.ErrorCode?.ToString(CultureInfo.InvariantCulture),
            ErrorMessage = resource.ErrorMessage,
            DateSent = ParseTwilioDate(resource.DateSent),
            DateCreated = ParseTwilioDate(resource.DateCreated)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioMessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        public List<TwilioMessageResource>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public int? Status { get; set; }
    }
}
