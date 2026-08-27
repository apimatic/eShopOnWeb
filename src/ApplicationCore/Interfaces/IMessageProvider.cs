using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio). Implementations must never throw for a
/// provider-rejected send; rejection is reported through the returned message state.
/// </summary>
public interface IMessageProvider
{
    /// <summary>Validates a destination number with the provider and returns its canonical form.</summary>
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber);

    /// <summary>Sends a message immediately, or queues it with the provider when <paramref name="sendAtUtc"/> is set.</summary>
    Task<ProviderMessage> SendMessageAsync(string toNumber, string body, DateTimeOffset? sendAtUtc = null);

    /// <summary>Asks the provider for the current state of a previously sent message.</summary>
    Task<ProviderMessage?> GetMessageAsync(string providerMessageSid);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task<ProviderMessage?> CancelScheduledMessageAsync(string providerMessageSid);

    /// <summary>Disposes of the message text at the provider so it is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string providerMessageSid);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given range. Covers the whole range (paging handled internally).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc);
}

public record ProviderMessage(
    string Sid,
    string Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public record PhoneNumberValidation(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);
