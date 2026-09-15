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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioSmsMessageGateway : ISmsMessageGateway
{
    public const string HttpClientName = "TwilioMessaging";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsMessageGateway> _logger;

    public TwilioSmsMessageGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> options,
        ILogger<TwilioSmsMessageGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<SmsMessageResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
            {
                fields["From"] = _settings.FromNumber;
            }

            try
            {
                return await PostMessageAsync(fields, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
            }
            catch (TwilioApiException)
            {
                fields.Remove("From");
                return await PostMessageAsync(fields, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
            }
        }

        fields["From"] = _settings.FromNumber;
        return await PostMessageAsync(fields, expectedStatus: System.Net.HttpStatusCode.Created, cancellationToken);
    }

    public async Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = MessagePath(messageSid);
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await SendWithoutLoggingBodyAsync(request, cancellationToken);
        var dto = await ReadMessageAsync(response, cancellationToken);
        return ToResult(dto);
    }

    public Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        };
        return PostMessageAsync(fields, System.Net.HttpStatusCode.OK, cancellationToken, MessagePath(messageSid));
    }

    public Task<SmsMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        };
        return PostMessageAsync(fields, System.Net.HttpStatusCode.OK, cancellationToken, MessagePath(messageSid));
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageResult>();
        var path = MessagesCollectionPath()
                   + "?From=" + Uri.EscapeDataString(fromNumber)
                   + "&" + Uri.EscapeDataString("DateSent>") + "=" + Uri.EscapeDataString(FormatRangeBound(from))
                   + "&" + Uri.EscapeDataString("DateSent<") + "=" + Uri.EscapeDataString(FormatRangeBound(to))
                   + "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = CreateRequest(HttpMethod.Get, path);
            using var response = await SendWithoutLoggingBodyAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Listing messages failed with status {StatusCode}.", (int)response.StatusCode);
                throw new TwilioApiException((int)response.StatusCode, "The messaging provider could not list messages.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, JsonOptions) ?? new TwilioMessageListDto();
            foreach (var message in list.Messages)
            {
                results.Add(ToResult(message));
            }

            path = ToRelativePath(list.NextPageUri);
        }

        return results;
    }

    private async Task<SmsMessageResult> PostMessageAsync(
        Dictionary<string, string> fields,
        System.Net.HttpStatusCode expectedStatus,
        CancellationToken cancellationToken,
        string? path = null)
    {
        path ??= MessagesCollectionPath();
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await SendWithoutLoggingBodyAsync(request, cancellationToken);
        var dto = await ReadMessageAsync(response, cancellationToken, expectedStatus);
        return ToResult(dto);
    }

    private async Task<TwilioMessageDto> ReadMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        System.Net.HttpStatusCode? expectedStatus = null)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((expectedStatus.HasValue && response.StatusCode != expectedStatus.Value) || !response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Messaging API call failed with status {StatusCode}.", (int)response.StatusCode);
            throw new TwilioApiException((int)response.StatusCode, "The messaging provider rejected the request.");
        }

        var dto = JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions);
        if (dto == null)
        {
            throw new TwilioApiException((int)response.StatusCode, "The messaging provider returned an empty message payload.");
        }

        return dto;
    }

    private async Task<HttpResponseMessage> SendWithoutLoggingBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await client.SendAsync(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = CreateAuthHeader();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagesCollectionPath()
        => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessagePath(string messageSid)
        => $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static string FormatRangeBound(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? ToRelativePath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return nextPageUri;
    }

    private static SmsMessageResult ToResult(TwilioMessageDto dto)
    {
        return new SmsMessageResult(
            dto.Sid,
            dto.Status,
            dto.Body,
            dto.From,
            dto.To,
            dto.ErrorCode,
            dto.ErrorMessage,
            dto.DateCreated,
            dto.DateSent,
            dto.DateUpdated);
    }
}
