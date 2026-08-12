using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin port over the SMS provider's messaging and lookup APIs. Everything eShop needs to know
/// about a message must be obtainable through this port, because the provider cannot call back into
/// this application (there is no publicly reachable URL for it).
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (E.164), for reporting context.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, for its canonical form.
    /// </summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Hand a message to the provider for immediate or scheduled sending.</summary>
    Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record of a message by its identifier.</summary>
    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redact a message's body at the provider so its text is no longer retrievable there, while the
    /// message record and its delivery outcome survive.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number within a date range. Covers the whole range (following pagination).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of validating a phone number against the provider.</summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors)
{
    public static PhoneNumberValidation Invalid(IReadOnlyList<string> errors) => new(false, null, errors);
    public static PhoneNumberValidation Valid(string canonical) => new(true, canonical, Array.Empty<string>());
}

/// <summary>A request to send one SMS.</summary>
public record SendSmsRequest(string To, string Body)
{
    /// <summary>
    /// When set, the provider is asked to schedule the send for this time rather than send now.
    /// </summary>
    public DateTimeOffset? SendAt { get; init; }
}

/// <summary>The provider's view of a message, as much of it as this integration reads.</summary>
public record ProviderMessage(
    string Sid,
    string? To,
    string? From,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);
