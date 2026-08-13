using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic gateway to the SMS messaging provider. The concrete implementation talks to
/// the provider strictly through its published contract (the OpenAPI specification in <c>api-specs</c>).
/// Nothing above this interface knows the provider is Twilio.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and, if so, returns its canonical
    /// E.164 form. Used to reject an unusable number at registration time rather than at send time.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now, from the application's configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for delivery at <paramref name="sendAt"/>. The provider holds
    /// and sends it; the application does not run a timer of its own.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Re-reads the provider's record for a message, returning its current delivery outcome.</summary>
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that was scheduled but has not yet been sent.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it can no longer be retrieved from the provider,
    /// while the provider's record that the message existed (and its outcome) survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending number
    /// within a date-time range. The filter is applied by the provider (by sending number), not by
    /// fetching a wider result and filtering afterwards, so other traffic on the account is excluded.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public class PhoneLookupResult
{
    public bool IsValid { get; init; }
    /// <summary>The provider's canonical E.164 form of the number (present when valid).</summary>
    public string? CanonicalNumber { get; init; }
    /// <summary>Reasons the number was considered invalid, if any.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

/// <summary>A snapshot of a message as the provider knows it.</summary>
public class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public string? MessagingServiceSid { get; init; }
}
