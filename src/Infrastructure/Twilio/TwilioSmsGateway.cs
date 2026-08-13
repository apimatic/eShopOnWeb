using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The messaging provider as this application needs it, implemented over the provider's 2010-04-01
/// Message resource. Maps this application's requests onto the contract's form fields and the contract's
/// message resource back onto <see cref="SmsDispatchResult"/> / <see cref="ProviderMessage"/>.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    /// <summary>A backstop on reconciliation paging so a runaway never loops forever.</summary>
    private const int MaxReconciliationPages = 100;
    private const int ReconciliationPageSize = 1000;

    private readonly TwilioMessagingClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioMessagingClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        var message = await _client.CreateAsync(form, cancellationToken);
        return Map(message);
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service feature: the sender is the service (no explicit From), and the
        // schedule is a fixed send time in ISO-8601. The provider queues and later sends the message.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };
        var message = await _client.CreateAsync(form, cancellationToken);
        return Map(message);
    }

    public async Task<SmsDispatchResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var message = await _client.FetchAsync(providerSid, cancellationToken);
        return Map(message);
    }

    public async Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        await _client.UpdateAsync(providerSid, form, cancellationToken);
    }

    public async Task DisposeContentAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // Redact the body at the provider by setting it to empty; the record of the message survives.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        await _client.UpdateAsync(providerSid, form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Broaden the provider-side date filter to whole GMT days so nothing inside the range is missed,
        // then filter each returned message precisely to the requested [from, to] window.
        var fromDay = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var results = new List<ProviderMessage>();
        string? url = _client.BuildListUrl(_settings.FromNumber, fromDay, toDay, ReconciliationPageSize);

        var pages = 0;
        while (!string.IsNullOrEmpty(url) && pages < MaxReconciliationPages)
        {
            var page = await _client.GetPageAsync(url!, cancellationToken);
            foreach (var m in page.Messages)
            {
                if (string.IsNullOrEmpty(m.Sid)) continue;
                var sent = ParseProviderDate(m.DateSent);
                if (!sent.HasValue || sent < from || sent > to) continue; // keep only messages sent within the range
                results.Add(new ProviderMessage(
                    m.Sid!, m.Status, m.To, m.From, sent, m.ErrorCode, PhoneNumberRedactor.Scrub(m.ErrorMessage)));
            }
            url = page.NextPageUri;
            pages++;
        }

        if (!string.IsNullOrEmpty(url))
        {
            // The range had more pages than the backstop allows; say so rather than silently truncating.
            _logger.LogWarning($"Reconciliation stopped after {MaxReconciliationPages} pages; the report for this range may be incomplete.");
        }

        return results;
    }

    private static SmsDispatchResult Map(TwilioMessageResource m) =>
        new(m.Sid, m.Status, m.ErrorCode, PhoneNumberRedactor.Scrub(m.ErrorMessage), ParseProviderDate(m.DateSent));

    private static DateTimeOffset? ParseProviderDate(string? rfc2822)
    {
        if (string.IsNullOrWhiteSpace(rfc2822)) return null;
        return DateTimeOffset.TryParse(rfc2822, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
