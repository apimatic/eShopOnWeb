using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never log
/// phone numbers or credentials.
/// </summary>
public interface ISmsClient
{
    /// <summary>
    /// Validates a raw caller-supplied number with the provider and returns the
    /// provider's canonical (E.164) form when usable.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately, or schedules it with the provider when
    /// <paramref name="sendAtUtc"/> is supplied. The provider owns the schedule;
    /// nothing is held in this application to be sent later.
    /// </summary>
    Task<SmsSendResult> SendMessageAsync(string toE164, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current authoritative state of one message.</summary>
    Task<SmsMessageState?> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task<bool> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the body of a message at the provider so the text is no longer retrievable there.</summary>
    Task<bool> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number within a UTC date range, following all pages.
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListMessagesFromSenderAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? NationalFormat { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}

public class SmsSendResult
{
    public bool Success { get; set; }
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
}

public class SmsMessageState
{
    public string MessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
