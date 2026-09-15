using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written client for Twilio Programmable Messaging (api.v2010 Messages)
/// as specified in api-specs/twilio/twilio_api_v2010. OperationIds: CreateMessage,
/// FetchMessage, UpdateMessage, ListMessage.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    public const string HttpClientName = "TwilioMessaging";

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
        ApplyAuthentication(_httpClient, _options);
    }

    public string FromNumber => _options.FromNumber;

    public async Task<SmsMessageResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            form["From"] = _options.FromNumber;
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
            }

            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        var resource = await ReadMessageResourceAsync(response, cancellationToken);
        return ToResult(resource);
    }

    public async Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstancePath(messageSid), cancellationToken);
        var resource = await ReadMessageResourceAsync(response, cancellationToken);
        return ToResult(resource);
    }

    public async Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        });
        using var response = await _httpClient.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);
        var resource = await ReadMessageResourceAsync(response, cancellationToken);
        return ToResult(resource);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        });
        using var response = await _httpClient.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageResult>();
        var pathAndQuery = MessagesCollectionPath()
            + "?From=" + Uri.EscapeDataString(fromNumber)
            + "&DateSent%3E=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
            + "&DateSent%3C=" + Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
            + "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(pathAndQuery))
        {
            var requestUri = ResolveMessagingUri(pathAndQuery);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioListMessageResponse>(payload, JsonOptions) ?? new TwilioListMessageResponse();

            foreach (var message in page.Messages)
            {
                results.Add(ToResult(message));
            }

            pathAndQuery = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    internal static void ApplyAuthentication(HttpClient httpClient, TwilioOptions options)
    {
        if (httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static string NormalizeBaseUrl(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "https://api.twilio.com/" : configured.Trim();
        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return value;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri ResolveMessagingUri(string pathOrAbsolute)
    {
        if (Uri.TryCreate(pathOrAbsolute, UriKind.Absolute, out var absolute))
        {
            if (_httpClient.BaseAddress is not null)
            {
                return new Uri(_httpClient.BaseAddress, absolute.PathAndQuery.TrimStart('/'));
            }

            return absolute;
        }

        return new Uri(_httpClient.BaseAddress ?? new Uri("https://api.twilio.com/"), pathOrAbsolute.TrimStart('/'));
    }

    private async Task<TwilioMessageResource> ReadMessageResourceAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions) ?? new TwilioMessageResource();
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? providerCode = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, JsonOptions);
            providerCode = error?.Code;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twilio error response could not be parsed. HTTP status {StatusCode}.", (int)response.StatusCode);
        }

        _logger.LogWarning("Twilio request failed with HTTP {StatusCode} and provider code {ProviderCode}.", (int)response.StatusCode, providerCode);
        throw new TwilioApiException(providerCode, response.StatusCode);
    }

    private static SmsMessageResult ToResult(TwilioMessageResource resource)
    {
        return new SmsMessageResult
        {
            Sid = resource.Sid,
            Status = resource.Status,
            Body = resource.Body,
            To = resource.To,
            From = resource.From,
            ErrorCode = resource.ErrorCode,
            ErrorMessage = resource.ErrorMessage,
            DateSent = ParseTwilioDate(resource.DateSent),
            DateCreated = ParseTwilioDate(resource.DateCreated)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
