using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>
/// Abstraction over the messaging provider. Every member maps to an operation in the provider's
/// OpenAPI contract (Twilio). Implementations live in the Infrastructure layer.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's configured sending number (Twilio:FromNumber), in E.164 form.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Validates a number with the provider and returns its canonical form. Used at registration so an
    /// unusable destination is rejected up front rather than when a message later fails to go out.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Throws <see cref="SmsGatewayException"/> if the provider will not accept it.</summary>
    Task<SentSmsMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/>. The provider — not this
    /// application — holds it until then. Throws <see cref="SmsGatewayException"/> on rejection.
    /// </summary>
    Task<SentSmsMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current delivery outcome for a message.</summary>
    Task<SmsMessageState> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Calls off a scheduled message at the provider before it goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider (redaction), leaving the record of the message and
    /// its outcome intact.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within the window. Filtering by sender is done by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
