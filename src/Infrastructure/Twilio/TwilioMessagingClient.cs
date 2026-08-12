using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Messaging gateway hand-written against twilio_api_v2010 (the Messages resource on api.twilio.com,
/// or the configured <c>Twilio:BaseUrl</c> override). The typed <see cref="HttpClient"/> is created
/// with its base address and Basic-auth header already configured (see Dependencies).
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;
    }

    private string MessagesPath => $"2010-04-01/Accounts/{RequireAccountSid()}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{RequireAccountSid()}/Messages/{sid}.json";

    public async Task<SmsMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var from = _settings.FromNumber;
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = from!,
            ["Body"] = body
        };

        using var response = await _http.PostAsync(MessagesPath, new FormUrlEncodedContent(form), cancellationToken);
        var message = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message created (sid {Sid}, status {Status}).", message.Sid, message.Status);
        return message;
    }

    public async Task<SmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt,
        CancellationToken cancellationToken = default)
    {
        var serviceSid = _settings.MessagingServiceSid;
        if (string.IsNullOrWhiteSpace(serviceSid))
        {
            // Scheduling requires a Messaging Service per the spec (ScheduleType/SendAt with MessagingServiceSid).
            throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = serviceSid!,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var response = await _http.PostAsync(MessagesPath, new FormUrlEncodedContent(form), cancellationToken);
        var message = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message scheduled (sid {Sid}, status {Status}).", message.Sid, message.Status);
        return message;
    }

    public async Task<SmsMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await _http.PostAsync(MessagePath(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        var message = await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message cancel requested (sid {Sid}, status {Status}).", message.Sid, message.Status);
        return message;
    }

    public async Task<SmsMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessagePath(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redaction: POST an empty Body per the spec, which removes the text but keeps the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await _http.PostAsync(MessagePath(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        await ReadMessageAsync(response, cancellationToken);
        _logger.LogInformation("Twilio message content redacted (sid {Sid}).", messageSid);
    }

    public async Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _settings.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        // Ask the provider for exactly this sending number's messages in the range (DateSent inclusive
        // bounds). Provider accounts carry other traffic, so we filter at the provider, not after.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        string? nextUri = $"{MessagesPath}?From={Uri.EscapeDataString(fromNumber!)}" +
                          $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromDate)}" +
                          $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toDate)}" +
                          "&PageSize=1000";

        var results = new List<SmsMessage>();
        var safetyMaxPages = 1000;

        while (!string.IsNullOrEmpty(nextUri) && safetyMaxPages-- > 0)
        {
            using var response = await _http.GetAsync(nextUri, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResponse>(cancellationToken: cancellationToken)
                       ?? new TwilioMessageListResponse();

            foreach (var m in page.Messages)
            {
                results.Add(ToSmsMessage(m));
            }

            nextUri = page.NextPageUri; // relative, e.g. /2010-04-01/Accounts/.../Messages.json?...&Page=1
        }

        return results;
    }

    private async Task<SmsMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(cancellationToken: cancellationToken);
        if (resource is null || string.IsNullOrEmpty(resource.Sid))
        {
            throw new TwilioApiException(response.StatusCode, null, "Twilio returned a message without an identifier.");
        }
        return ToSmsMessage(resource);
    }

    private static SmsMessage ToSmsMessage(TwilioMessageResource m) => new()
    {
        Sid = m.Sid ?? string.Empty,
        Status = m.Status ?? string.Empty,
        To = m.To,
        From = m.From,
        ErrorCode = m.ErrorCode,
        ErrorMessage = m.ErrorMessage,
        DateSent = ParseRfc2822(m.DateSent)
    };

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        string message = $"Twilio request failed with HTTP {(int)response.StatusCode}.";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResource>(cancellationToken: cancellationToken);
            if (error is not null)
            {
                code = error.Code;
                if (!string.IsNullOrWhiteSpace(error.Message))
                {
                    message = error.Message!;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        throw new TwilioApiException(response.StatusCode, code, message);
    }

    private string RequireAccountSid()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid))
        {
            throw new InvalidOperationException("Twilio:AccountSid is not configured.");
        }
        return _settings.AccountSid!;
    }
}
