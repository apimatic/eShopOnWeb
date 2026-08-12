using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// <see cref="ISmsGateway"/> implemented by hand directly against Twilio's OpenAPI specification
/// (<c>api-specs/twilio</c>). No pre-built Twilio SDK is used.
/// <list type="bullet">
/// <item>Number validation → Lookups v2: <c>GET https://lookups.twilio.com/v2/PhoneNumbers/{number}</c>.</item>
/// <item>Send / schedule / cancel / redact / fetch / list → API 2010-04-01 Messages resource on the
/// messaging base (default <c>https://api.twilio.com</c>, overridable by <c>Twilio:BaseUrl</c>).</item>
/// </list>
/// Auth is HTTP Basic (AccountSid:AuthToken), configured once on the injected <see cref="HttpClient"/>.
/// The auth token and shopper phone numbers are never logged.
/// </summary>
public class TwilioMessagingGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageInstanceUrl(string sid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookups is served from its own host and is NOT governed by Twilio:BaseUrl.
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var dto = Deserialize<TwilioLookupDto>(payload);
            var valid = dto?.Valid == true;
            return new PhoneNumberValidationResult(valid, valid ? dto?.PhoneNumber : null,
                dto?.ValidationErrors ?? new List<string>());
        }

        // A number the provider cannot process (e.g. malformed) is reported as not valid, not an outage.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var error = Deserialize<TwilioErrorDto>(payload);
            var reason = error?.Message is { Length: > 0 } ? error.Message! : "not a recognisable number";
            return new PhoneNumberValidationResult(false, null, new List<string> { reason });
        }

        throw ToException("validate the phone number", response.StatusCode, payload);
    }

    public async Task<SmsMessageState> SendAsync(SmsMessageRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["To"] = request.To, ["Body"] = request.Body };

        if (request.SendAt.HasValue)
        {
            // Scheduling requires a Messaging Service; the message is queued with the provider for later.
            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else
        {
            // Immediate messages go out from this application's own number, so reconciliation attributes them.
            form["From"] = _settings.FromNumber;
        }

        var dto = await PostFormAsync(MessagesCollectionUrl, form, "send the message", cancellationToken);
        return ToState(dto);
    }

    public async Task<SmsMessageState> GetMessageStateAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstanceUrl(providerMessageId));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException("fetch the message", response.StatusCode, payload);
        }

        return ToState(Deserialize<TwilioMessageDto>(payload));
    }

    public async Task<SmsMessageState> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // Update the message status to canceled — the provider calls off a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var dto = await PostFormAsync(MessageInstanceUrl(providerMessageId), form, "cancel the scheduled message", cancellationToken);
        return ToState(dto);
    }

    public async Task RedactContentAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // Redact the body at the provider by updating it to an empty string. The record itself survives.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostFormAsync(MessageInstanceUrl(providerMessageId), form, "redact the message content", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListOutboundMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Ask the provider for THIS number's messages over the range (server-side From + DateSent filters),
        // rather than filtering a wider answer after the fact. The classic API's DateSent bounds are
        // date-granular and treat a date value as that day's 00:00, so an inclusive window needs the lower
        // bound at the 'from' day and the upper bound at the day AFTER 'to'; we then trim to the exact
        // instant window in memory.
        var fromDate = from.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var url = $"{MessagesCollectionUrl}" +
                  $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                  $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromDate)}" +
                  $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toDate)}" +
                  $"&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        string? nextUrl = url;
        var pages = 0;

        while (nextUrl is not null && pages < MaxReconciliationPages)
        {
            pages++;
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw ToException("list messages for reconciliation", response.StatusCode, payload);
            }

            var page = Deserialize<TwilioMessageListDto>(payload);
            if (page?.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    if (string.IsNullOrEmpty(message.Sid))
                    {
                        continue;
                    }

                    var dateSent = ParseTwilioDate(message.DateSent);
                    // Trim to the exact requested window; a message with no send date was not actually sent.
                    if (!dateSent.HasValue || dateSent.Value < from || dateSent.Value > to)
                    {
                        continue;
                    }

                    results.Add(new ProviderMessageRecord(
                        message.Sid!, message.Status, MapStatus(message.Status), message.To, message.From,
                        message.ErrorCode, message.ErrorMessage, dateSent, ParseTwilioDate(message.DateCreated)));
                }
            }

            nextUrl = string.IsNullOrEmpty(page?.NextPageUri) ? null : MessagingBaseUrl + page!.NextPageUri;
        }

        return results;
    }

    // ---- helpers ----

    private async Task<TwilioMessageDto?> PostFormAsync(string url, IDictionary<string, string> form, string action,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(action, response.StatusCode, payload);
        }

        return Deserialize<TwilioMessageDto>(payload);
    }

    private static SmsMessageState ToState(TwilioMessageDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.Sid))
        {
            throw new SmsGatewayException("The provider returned a message without an identifier.");
        }

        return new SmsMessageState(
            dto.Sid!,
            MapStatus(dto.Status),
            dto.Status,
            dto.ErrorCode,
            dto.ErrorMessage,
            ParseTwilioDate(dto.DateSent));
    }

    private static NotificationDeliveryStatus MapStatus(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
    {
        "queued" => NotificationDeliveryStatus.Queued,
        "sending" => NotificationDeliveryStatus.Sending,
        "sent" => NotificationDeliveryStatus.Sent,
        "delivered" => NotificationDeliveryStatus.Delivered,
        "undelivered" => NotificationDeliveryStatus.Undelivered,
        "failed" => NotificationDeliveryStatus.Failed,
        "accepted" => NotificationDeliveryStatus.Accepted,
        "scheduled" => NotificationDeliveryStatus.Scheduled,
        "canceled" => NotificationDeliveryStatus.Canceled,
        "cancelled" => NotificationDeliveryStatus.Canceled,
        "read" => NotificationDeliveryStatus.Read,
        "partially_delivered" => NotificationDeliveryStatus.PartiallyDelivered,
        _ => NotificationDeliveryStatus.Queued
    };

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio timestamps are RFC 2822 (e.g. "Thu, 24 Aug 2023 05:01:45 +0000").
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

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

    private static SmsGatewayException ToException(string action, HttpStatusCode statusCode, string payload)
    {
        var error = Deserialize<TwilioErrorDto>(payload);
        if (error?.Code is not null || error?.Message is not null)
        {
            return new SmsGatewayException(
                $"Could not {action}: the provider returned {(int)statusCode} (code {error.Code}): {error.Message}",
                error.Code);
        }

        return new SmsGatewayException($"Could not {action}: the provider returned HTTP {(int)statusCode}.");
    }
}
