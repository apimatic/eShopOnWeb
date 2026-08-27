using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against
/// api-specs/twilio/twilio_api_v2010 (Messages resource). Auth is HTTP Basic
/// with the account SID and auth token, per the spec's security scheme.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    public const string HttpClientName = "Twilio";
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    private string MessagingBaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? DefaultMessagingBaseUrl
        : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    public async Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        var message = await PostMessageAsync(MessagesUrl, form, "CreateMessage", cancellationToken);
        return new SmsSendResult(message.Sid!, message.Status!);
    }

    public async Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Services capability per the spec (ScheduleType: fixed).
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        var message = await PostMessageAsync(MessagesUrl, form, "CreateMessage", cancellationToken);
        return new SmsSendResult(message.Sid!, message.Status!);
    }

    public async Task<SmsSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var message = await PostMessageAsync(MessageUrl(messageSid), form, "UpdateMessage", cancellationToken);
        return new SmsSendResult(message.Sid!, message.Status!);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(messageSid), form, "UpdateMessage", cancellationToken);
    }

    public async Task<SmsMessageDetails> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.GetAsync(MessageUrl(messageSid), cancellationToken);
        await EnsureSuccessAsync(response, "FetchMessage", cancellationToken);
        var message = (await response.Content.ReadFromJsonAsync<TwilioMessage>(cancellationToken: cancellationToken))!;
        return ToDetails(message);
    }

    public async Task<IReadOnlyList<SmsMessageDetails>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's messages,
        // server-side, per the ListMessage operation's From/DateSent filters.
        var query = new Dictionary<string, string>
        {
            ["From"] = _settings.FromNumber,
            ["DateSent>"] = FormatDateSentFilter(from),
            ["DateSent<"] = FormatDateSentFilter(to),
            ["PageSize"] = "1000"
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var results = new List<SmsMessageDetails>();
        string? nextUri = MessagesUrl + BuildQueryString(query);

        while (!string.IsNullOrEmpty(nextUri))
        {
            var requestUri = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : MessagingBaseUrl + nextUri;
            var response = await client.GetAsync(requestUri, cancellationToken);
            await EnsureSuccessAsync(response, "ListMessage", cancellationToken);
            var page = (await response.Content.ReadFromJsonAsync<TwilioListMessageResponse>(cancellationToken: cancellationToken))!;
            results.AddRange(page.Messages.Select(ToDetails));
            nextUri = page.NextPageUri;
        }

        return results;
    }

    private string MessageUrl(string messageSid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private async Task<TwilioMessage> PostMessageAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // A freshly created (scheduled) message can transiently 404 on update while
        // the provider's stores converge; retry that case a few times.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var response = await client.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && operation == "UpdateMessage" && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
                continue;
            }

            await EnsureSuccessAsync(response, operation, cancellationToken);
            return (await response.Content.ReadFromJsonAsync<TwilioMessage>(cancellationToken: cancellationToken))!;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? twilioErrorCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(cancellationToken: cancellationToken);
            twilioErrorCode = error?.Code;
        }
        catch
        {
            // Error body wasn't the provider's JSON error model; the HTTP status is enough.
        }

        throw new TwilioApiException(response.StatusCode, twilioErrorCode, operation);
    }

    private static string FormatDateSentFilter(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string BuildQueryString(Dictionary<string, string> parameters) =>
        "?" + string.Join("&", parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static SmsMessageDetails ToDetails(TwilioMessage message) => new()
    {
        MessageSid = message.Sid ?? string.Empty,
        To = message.To ?? string.Empty,
        From = message.From ?? string.Empty,
        Status = message.Status ?? string.Empty,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        DateCreated = ParseRfc2822(message.DateCreated),
        DateSent = ParseRfc2822(message.DateSent),
        Body = message.Body
    };

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
