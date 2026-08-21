using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioSettings> _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public static Uri CreateBaseAddress(string? configuredBaseUrl)
        => TwilioRequestHelper.ResolveMessagingBaseAddress(configuredBaseUrl);

    public string ConfiguredFromNumber => _options.Value.FromNumber;

    public async Task<TwilioSendResult> SendAsync(TwilioSendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var settings = RequireMessagingSettings();
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = settings.FromNumber,
            ["MessagingServiceSid"] = settings.MessagingServiceSid,
            ["SmartEncoded"] = "true"
        };

        if (request.SendAt.HasValue)
        {
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        try
        {
            using var response = await SendFormAsync(HttpMethod.Post, MessagesCollectionPath(settings.AccountSid), fields, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode == 201)
            {
                var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioRequestHelper.JsonOptions);
                return new TwilioSendResult
                {
                    Accepted = true,
                    Message = ToSnapshot(resource)
                };
            }

            var error = TwilioRequestHelper.TryReadError(payload);
            _logger.LogWarning("Twilio Create Message failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
            return new TwilioSendResult
            {
                Accepted = false,
                ErrorCode = error?.Code,
                ErrorStatus = "failed"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Twilio Create Message threw before a provider response was received.");
            return new TwilioSendResult
            {
                Accepted = false,
                ErrorStatus = "failed"
            };
        }
    }

    public async Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var settings = RequireMessagingSettings();
        using var response = await SendAsync(HttpMethod.Get, MessageResourcePath(settings.AccountSid, messageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TwilioRequestHelper.TryReadError(payload);
            _logger.LogWarning("Twilio Fetch Message failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioRequestHelper.JsonOptions);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot?> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var settings = RequireMessagingSettings();
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await SendFormAsync(HttpMethod.Post, MessageResourcePath(settings.AccountSid, messageSid), fields, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TwilioRequestHelper.TryReadError(payload);
            _logger.LogWarning("Twilio cancel Message failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
            return null;
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioRequestHelper.JsonOptions);
        return ToSnapshot(resource);
    }

    public async Task<TwilioMessageSnapshot?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var settings = RequireMessagingSettings();
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await SendFormAsync(HttpMethod.Post, MessageResourcePath(settings.AccountSid, messageSid), fields, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TwilioRequestHelper.TryReadError(payload);
            _logger.LogWarning("Twilio redact Message failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
            throw new InvalidOperationException("The provider could not dispose of the message content.");
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioRequestHelper.JsonOptions);
        return ToSnapshot(resource);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var settings = RequireMessagingSettings();
        var fromDate = from.ToUniversalTime().UtcDateTime.Date;
        var toDate = to.ToUniversalTime().UtcDateTime.Date;
        var dateSentAfter = fromDate.ToString("yyyy-MM-dd");
        var dateSentBefore = toDate.AddDays(1).ToString("yyyy-MM-dd");
        var query =
            $"From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(dateSentAfter)}" +
            $"&DateSent%3C={Uri.EscapeDataString(dateSentBefore)}" +
            "&PageSize=1000";

        string? path = $"{MessagesCollectionPath(settings.AccountSid)}?{query}";
        var results = new List<TwilioMessageSnapshot>();

        while (!string.IsNullOrEmpty(path))
        {
            using (var response = await SendAsync(HttpMethod.Get, path, cancellationToken))
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = TwilioRequestHelper.TryReadError(payload);
                    _logger.LogWarning("Twilio List Message failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
                    throw new InvalidOperationException("The provider message list could not be retrieved.");
                }

                var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, TwilioRequestHelper.JsonOptions);
                if (page?.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        var snapshot = ToSnapshot(message);
                        if (snapshot is not null)
                        {
                            results.Add(snapshot);
                        }
                    }
                }

                path = string.IsNullOrEmpty(page?.NextPageUri) ? null : page!.NextPageUri;
            }
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var uri = TwilioRequestHelper.Combine(MessagingBaseAddress(), pathAndQuery);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = TwilioRequestHelper.CreateBasicAuth(settings);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendFormAsync(HttpMethod method, string pathAndQuery, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var uri = TwilioRequestHelper.Combine(MessagingBaseAddress(), pathAndQuery);
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = TwilioRequestHelper.CreateBasicAuth(settings);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private Uri MessagingBaseAddress()
    {
        if (_httpClient.BaseAddress is not null)
        {
            return _httpClient.BaseAddress;
        }

        return TwilioRequestHelper.ResolveMessagingBaseAddress(_options.Value.BaseUrl);
    }

    private TwilioSettings RequireMessagingSettings()
    {
        var settings = _options.Value;
        TwilioRequestHelper.EnsureCredentials(settings);
        if (string.IsNullOrWhiteSpace(settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.AccountSid))
        {
            throw new InvalidOperationException("Twilio:AccountSid is not configured.");
        }

        return settings;
    }

    private static string MessagesCollectionPath(string accountSid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json";

    private static string MessageResourcePath(string accountSid, string messageSid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static TwilioMessageSnapshot? ToSnapshot(TwilioMessageResource? resource)
    {
        if (resource is null)
        {
            return null;
        }

        return new TwilioMessageSnapshot
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
}
