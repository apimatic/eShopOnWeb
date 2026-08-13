using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Sends, reads, cancels, redacts and lists messages through Twilio's messaging API (the classic
/// <c>2010-04-01</c> Message resource), built by hand against the Twilio OpenAPI spec. Auth (HTTP Basic
/// with the account SID and auth token) and the base address are configured on the injected
/// <see cref="HttpClient"/>; this class never logs the auth token, message bodies or destination numbers.
/// </summary>
public class TwilioMessagingClient : ISmsSender
{
    private const string MessagesPath = "2010-04-01/Accounts/{0}/Messages.json";
    private const string MessageInstancePath = "2010-04-01/Accounts/{0}/Messages/{1}.json";
    private const int MaxPages = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body)
        };

        if (request.ScheduleFor.HasValue)
        {
            // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", ToIso8601(request.ScheduleFor.Value)));
        }
        else
        {
            form.Add(new("From", _settings.FromNumber));
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesUrl(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body);
        }

        var message = Deserialize<TwilioMessageResource>(body);
        return ToSmsMessage(message);
    }

    public async Task<SmsMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstanceUrl(messageSid), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body);
        }

        var message = Deserialize<TwilioMessageResource>(body);
        return ToSmsMessage(message);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        using var response = await _httpClient.PostAsync(MessageInstanceUrl(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw BuildException(response.StatusCode, body);
        }
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redacting the body (empty string) disposes of the message content at the provider while the
        // record of the message and its outcome survives.
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        using var response = await _httpClient.PostAsync(MessageInstanceUrl(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw BuildException(response.StatusCode, body);
        }
    }

    public async Task<IReadOnlyList<SmsMessage>> ListAsync(SmsListFilter filter, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessage>();
        var nextUrl = BuildListUrl(filter);
        var page = 0;

        while (nextUrl is not null && page < MaxPages)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response.StatusCode, body);
            }

            var listResponse = Deserialize<TwilioListMessagesResponse>(body);
            if (listResponse.Messages is not null)
            {
                foreach (var message in listResponse.Messages)
                {
                    results.Add(ToSmsMessage(message));
                }
            }

            nextUrl = ResolveNextPage(listResponse.NextPageUri);
            page++;
        }

        return results;
    }

    // ----- URL building -----

    private string MessagesUrl() => string.Format(CultureInfo.InvariantCulture, MessagesPath, _settings.AccountSid);

    private string MessageInstanceUrl(string sid) =>
        string.Format(CultureInfo.InvariantCulture, MessageInstancePath, _settings.AccountSid, sid);

    private string BuildListUrl(SmsListFilter filter)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(filter.From))
        {
            query.Add("From=" + Uri.EscapeDataString(filter.From));
        }
        if (filter.DateSentAfter.HasValue)
        {
            // "on and after" the range start.
            query.Add(Uri.EscapeDataString("DateSent>") + "=" + Uri.EscapeDataString(ToIso8601(filter.DateSentAfter.Value)));
        }
        if (filter.DateSentBefore.HasValue)
        {
            // "on and before" the range end.
            query.Add(Uri.EscapeDataString("DateSent<") + "=" + Uri.EscapeDataString(ToIso8601(filter.DateSentBefore.Value)));
        }
        query.Add("PageSize=1000");

        return MessagesUrl() + "?" + string.Join("&", query);
    }

    /// <summary>
    /// Resolves the provider's next-page URI against the configured messaging base so pagination keeps
    /// using the same (possibly overridden) messaging host.
    /// </summary>
    private string? ResolveNextPage(string? nextPageUri)
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

    // ----- mapping / parsing -----

    private static SmsMessage ToSmsMessage(TwilioMessageResource resource) => new()
    {
        Sid = resource.Sid ?? string.Empty,
        Status = resource.Status,
        From = resource.From,
        To = resource.To,
        Body = resource.Body,
        ErrorCode = resource.ErrorCode,
        ErrorMessage = resource.ErrorMessage,
        DateSent = ParseDate(resource.DateSent),
        DateCreated = ParseDate(resource.DateCreated)
    };

    private static string ToIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static T Deserialize<T>(string body)
    {
        var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (value is null)
        {
            throw new TwilioApiException(0, null, "The Twilio response could not be parsed.");
        }
        return value;
    }

    private static TwilioApiException BuildException(HttpStatusCode statusCode, string body)
    {
        TwilioErrorResponse? error = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall back to a generic message below.
            }
        }

        var message = error?.Message ?? $"Twilio returned {(int)statusCode}.";
        return new TwilioApiException((int)statusCode, error?.Code, message, error?.MoreInfo);
    }
}
