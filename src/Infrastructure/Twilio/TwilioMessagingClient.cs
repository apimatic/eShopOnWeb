using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Messaging API client for CreateMessage, FetchMessage, UpdateMessage, and ListMessage
/// as defined by api-specs/twilio/twilio_api_v2010.
/// </summary>
public class TwilioMessagingClient : ISmsMessageGateway
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            // ScheduleType=fixed + SendAt requires MessagingServiceSid (CreateMessage in twilio_api_v2010).
            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (!string.IsNullOrWhiteSpace(_options.FromNumber))
            {
                fields["From"] = _options.FromNumber;
            }
        }
        else
        {
            fields["From"] = _options.FromNumber;
        }

        var (statusCode, payload) = await SendFormAsync(
            HttpMethod.Post,
            MessagesCollectionPath(),
            fields,
            "CreateMessage",
            cancellationToken);

        if (!IsSuccess(statusCode))
        {
            throw ToGatewayException("CreateMessage", statusCode, Deserialize<TwilioErrorDto>(payload));
        }

        var dto = Deserialize<TwilioMessageDto>(payload)
            ?? throw new SmsGatewayException("CreateMessage did not return a message resource.", (int)statusCode);
        return ToSnapshot(dto);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var (statusCode, payload) = await SendAsync(
            HttpMethod.Get,
            MessageInstancePath(providerMessageSid),
            content: null,
            "FetchMessage",
            cancellationToken);

        if (statusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!IsSuccess(statusCode))
        {
            throw ToGatewayException("FetchMessage", statusCode, Deserialize<TwilioErrorDto>(payload));
        }

        var dto = Deserialize<TwilioMessageDto>(payload);
        return dto is null ? null : ToSnapshot(dto);
    }

    public async Task<SmsMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        };

        var (statusCode, payload) = await SendFormAsync(
            HttpMethod.Post,
            MessageInstancePath(providerMessageSid),
            fields,
            "UpdateMessage",
            cancellationToken);

        if (!IsSuccess(statusCode))
        {
            throw ToGatewayException("UpdateMessage", statusCode, Deserialize<TwilioErrorDto>(payload));
        }

        var dto = Deserialize<TwilioMessageDto>(payload)
            ?? throw new SmsGatewayException("UpdateMessage did not return a message resource.", (int)statusCode);
        return ToSnapshot(dto);
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        };

        var (statusCode, payload) = await SendFormAsync(
            HttpMethod.Post,
            MessageInstancePath(providerMessageSid),
            fields,
            "UpdateMessage",
            cancellationToken);

        if (!IsSuccess(statusCode))
        {
            throw ToGatewayException("UpdateMessage", statusCode, Deserialize<TwilioErrorDto>(payload));
        }

        var dto = Deserialize<TwilioMessageDto>(payload)
            ?? throw new SmsGatewayException("UpdateMessage did not return a message resource.", (int)statusCode);
        return ToSnapshot(dto);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentByConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromValue = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toValue = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var path = MessagesCollectionPath()
            + "?From=" + Uri.EscapeDataString(_options.FromNumber)
            + "&DateSent>=" + Uri.EscapeDataString(fromValue)
            + "&DateSent<=" + Uri.EscapeDataString(toValue)
            + "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(path))
        {
            var (statusCode, payload) = await SendAsync(HttpMethod.Get, path, content: null, "ListMessage", cancellationToken);
            if (!IsSuccess(statusCode))
            {
                throw ToGatewayException("ListMessage", statusCode, Deserialize<TwilioErrorDto>(payload));
            }

            var page = Deserialize<TwilioMessageListDto>(payload) ?? new TwilioMessageListDto();
            foreach (var message in page.Messages)
            {
                results.Add(ToSnapshot(message));
            }

            path = NormalizeNextPagePath(page.NextPageUri);
        }

        return results;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    private async Task<(System.Net.HttpStatusCode StatusCode, string Payload)> SendFormAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        using var content = CreateFormContent(fields);
        return await SendAsync(method, path, content, operation, cancellationToken);
    }

    private static HttpContent CreateFormContent(IReadOnlyDictionary<string, string> fields)
    {
        // FormUrlEncodedContent can omit empty values; redaction requires Body as an empty string
        // per UpdateMessage in twilio_api_v2010.
        var encoded = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));
        return new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
    }

    private async Task<(System.Net.HttpStatusCode StatusCode, string Payload)> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, ResolveUri(path));
        if (content is not null)
        {
            request.Content = content;
        }

        ApplyBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("Twilio {Operation} returned {StatusCode}.", operation, (int)response.StatusCode);
        return (response.StatusCode, payload);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private Uri ResolveUri(string pathAndQuery)
    {
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out var absolute))
        {
            pathAndQuery = absolute.PathAndQuery.TrimStart('/');
        }
        else
        {
            pathAndQuery = pathAndQuery.TrimStart('/');
        }

        return new Uri(_httpClient.BaseAddress ?? new Uri(DefaultBaseUrl), pathAndQuery);
    }

    private static string? NormalizeNextPagePath(string? nextPageUri)
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

    private static bool IsSuccess(System.Net.HttpStatusCode statusCode)
        => (int)statusCode is >= 200 and <= 299;

    private static T? Deserialize<T>(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static SmsGatewayException ToGatewayException(string operation, System.Net.HttpStatusCode statusCode, TwilioErrorDto? error)
    {
        var message = PhoneNumberLogRedactor.Redact(error?.Message) is { Length: > 0 } redacted
            ? $"{operation} failed: {redacted}"
            : $"{operation} failed with HTTP {(int)statusCode}.";
        return new SmsGatewayException(message, (int)statusCode, error?.Code);
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageDto dto)
        => new()
        {
            Sid = dto.Sid,
            Status = dto.Status,
            Body = dto.Body,
            From = dto.From,
            To = dto.To,
            ErrorCode = dto.ErrorCode,
            ErrorMessage = PhoneNumberLogRedactor.Redact(dto.ErrorMessage),
            DateSent = dto.DateSent,
            DateCreated = dto.DateCreated,
            Direction = dto.Direction
        };
}
