using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Implementations must never log
/// phone numbers or credentials.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Validates a phone number with the provider and returns its canonical form.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message immediately, or queues it with the provider for <paramref name="sendAt"/>
    /// when supplied. Returns the provider's acceptance result; a provider-side rejection is
    /// reported on the result, not thrown.
    /// </summary>
    Task<SendMessageResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current state for one message.</summary>
    Task<ProviderMessageState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that has been queued with the provider for later delivery.</summary>
    Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within a date range, covering every page of the range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageState>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? NationalFormat { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}

public class SendMessageResult
{
    public bool Accepted { get; set; }
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ProviderMessageState
{
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
