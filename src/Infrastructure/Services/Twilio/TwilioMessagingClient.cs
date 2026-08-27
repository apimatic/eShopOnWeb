using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const int MaxListPages = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioSettings> _options;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var fields = ImmediateMessageFields(settings, to, body);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
        {
            throw new TwilioMessagingException("Twilio MessagingServiceSid is not configured.");
        }

        var fields = ImmediateMessageFields(settings, to, body);
        fields["MessagingServiceSid"] = settings.MessagingServiceSid;
        fields["ScheduleType"] = "fixed";
        fields["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var url = TwilioHttp.Combine(TwilioHttp.MessagingBaseUrl(settings), MessageInstancePath(settings.AccountSid, messageSid));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        TwilioHttp.ApplyBasicAuth(request, settings);

        using var response = await SendWithoutLeakingAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return Map(DeserializeMessage(payload));
    }

    public Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        if (string.IsNullOrWhiteSpace(settings.FromNumber))
        {
            throw new TwilioMessagingException("Twilio FromNumber is not configured.");
        }

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid)}/Messages.json";
        var query =
            $"From={Uri.EscapeDataString(settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(ToTwilioTimestamp(from))}" +
            $"&DateSent%3C={Uri.EscapeDataString(ToTwilioTimestamp(to))}" +
            "&PageSize=1000";
        var next = path + "?" + query;

        var results = new List<TwilioMessageSnapshot>();
        var baseUrl = TwilioHttp.MessagingBaseUrl(settings);

        for (var page = 0; page < MaxListPages && !string.IsNullOrEmpty(next); page++)
        {
            var url = TwilioHttp.Combine(baseUrl, next);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            TwilioHttp.ApplyBasicAuth(request, settings);
            using var response = await SendWithoutLeakingAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            var list = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                       ?? new TwilioMessageListResponse();
            foreach (var message in list.Messages)
            {
                results.Add(Map(message));
            }

            next = list.NextPageUri;
        }

        return results;
    }

    private static Dictionary<string, string> ImmediateMessageFields(TwilioSettings settings, string to, string body)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(settings.FromNumber))
        {
            fields["From"] = settings.FromNumber;
        }

        return fields;
    }

    private async Task<TwilioMessageSnapshot> CreateMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var settings = RequireSettings();
        var url = TwilioHttp.Combine(
            TwilioHttp.MessagingBaseUrl(settings),
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid)}/Messages.json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        TwilioHttp.ApplyBasicAuth(request, settings);

        using var response = await SendWithoutLeakingAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return Map(DeserializeMessage(payload));
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        var settings = RequireSettings();
        var url = TwilioHttp.Combine(TwilioHttp.MessagingBaseUrl(settings), MessageInstancePath(settings.AccountSid, messageSid));
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        TwilioHttp.ApplyBasicAuth(request, settings);

        using var response = await SendWithoutLeakingAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return Map(DeserializeMessage(payload));
    }

    private TwilioSettings RequireSettings()
    {
        var settings = _options.Value;
        TwilioHttp.EnsureConfigured(settings);
        return settings;
    }

    private async Task<HttpResponseMessage> SendWithoutLeakingAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Twilio messaging request failed to complete.");
            throw new TwilioMessagingException("Twilio messaging request failed to complete.");
        }
    }

    private void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _logger.LogWarning("Twilio messaging request returned HTTP {StatusCode}.", (int)response.StatusCode);
        _ = payload;
        throw new TwilioMessagingException("Twilio messaging request was not successful.");
    }

    private static TwilioMessageResponse DeserializeMessage(string payload)
    {
        return JsonSerializer.Deserialize<TwilioMessageResponse>(payload, JsonOptions)
               ?? throw new TwilioMessagingException("Twilio messaging request returned an empty body.");
    }

    private static TwilioMessageSnapshot Map(TwilioMessageResponse response)
    {
        return new TwilioMessageSnapshot
        {
            Sid = response.Sid ?? string.Empty,
            Status = response.Status ?? string.Empty,
            ErrorCode = FormatErrorCode(response.ErrorCode),
            Body = response.Body,
            DateCreated = ParseTwilioDate(response.DateCreated),
            DateSent = ParseTwilioDate(response.DateSent)
        };
    }

    private static string? FormatErrorCode(object? errorCode)
    {
        if (errorCode is null)
        {
            return null;
        }

        if (errorCode is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String => element.GetString(),
                _ => element.ToString()
            };
        }

        var text = errorCode.ToString();
        return string.IsNullOrWhiteSpace(text) || text == "null" ? null : text;
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

    private static string ToTwilioTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string MessageInstancePath(string accountSid, string messageSid)
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
}
