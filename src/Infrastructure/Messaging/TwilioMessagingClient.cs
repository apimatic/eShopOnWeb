using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio Programmable Messaging client. Contract: api-specs/twilio/twilio_api_v2010/twilio_api_v2010.yaml
/// Messages under /2010-04-01/Accounts/{AccountSid}/Messages.json on the messaging host
/// (servers.url https://api.twilio.com, overridden by Twilio:BaseUrl when set).
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public string ConfiguredFromNumber => _settings.FromNumber;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        TwilioAuth.EnsureConfigured(_settings);

        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            form["From"] = _settings.FromNumber;
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio MessagingServiceSid is required to queue a follow-up message with the provider.");
            }

            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = new FormUrlEncodedContent(form)
        };
        httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

        return await SendAndReadMessageAsync(httpRequest, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        TwilioAuth.EnsureConfigured(_settings);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(messageSid));
        httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

        return await SendAndReadMessageAsync(httpRequest, cancellationToken);
    }

    public async Task<ProviderMessage> UpdateAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default)
    {
        TwilioAuth.EnsureConfigured(_settings);

        var form = new Dictionary<string, string>();
        if (body is not null)
        {
            form["Body"] = body;
        }

        if (status is not null)
        {
            form["Status"] = status;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(messageSid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

        return await SendAndReadMessageAsync(httpRequest, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        TwilioAuth.EnsureConfigured(_settings);

        var query = string.Join("&",
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}",
            "PageSize=1000");

        var pathAndQuery = $"{MessagesCollectionPath()}?{query}";
        var results = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(pathAndQuery))
        {
            var uri = TwilioAuth.ResolveAgainstBase(RequireBaseAddress(), pathAndQuery);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            httpRequest.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Provider message list returned HTTP {StatusCode}.", (int)response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var page = await response.Content.ReadFromJsonAsync<ListMessageResponseDto>(JsonOptions, cancellationToken);
            if (page?.Messages is not null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(ToProviderMessage(item));
                }
            }

            pathAndQuery = page?.NextPageUri;
        }

        return results;
    }

    private async Task<ProviderMessage> SendAndReadMessageAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Provider messaging request {Method} returned HTTP {StatusCode}.", httpRequest.Method, (int)response.StatusCode);
            throw new HttpRequestException($"Twilio messaging request failed with status {(int)response.StatusCode}.");
        }

        var dto = await response.Content.ReadFromJsonAsync<TwilioMessageDto>(JsonOptions, cancellationToken);
        if (dto is null || string.IsNullOrEmpty(dto.Sid))
        {
            throw new InvalidOperationException("Twilio messaging request returned an empty message resource.");
        }

        return ToProviderMessage(dto);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private Uri RequireBaseAddress()
        => _httpClient.BaseAddress
           ?? throw new InvalidOperationException("Twilio messaging HttpClient BaseAddress is not configured.");

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto)
        => new(
            dto.Sid ?? string.Empty,
            dto.Status,
            dto.Body,
            dto.To,
            dto.From,
            dto.ErrorCode,
            dto.ErrorMessage,
            dto.DateSent,
            dto.DateCreated);

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class ListMessageResponseDto
    {
        public List<TwilioMessageDto>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }
}
