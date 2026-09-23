using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary over the SMS provider. Every method translates provider/transport failures to
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.SmsGatewayException"/>; callers decide whether a
/// failure should surface or simply be recorded (a failed notification never fails the order operation).
/// </summary>
public interface ISmsGateway
{
    /// <summary>Validate and canonicalize a number. Returns the provider's canonical E.164 form when valid.</summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Send a message now, from the configured sending number.</summary>
    Task<SentMessage> SendAsync(string toNumber, string body, CancellationToken ct);

    /// <summary>Queue a delivery follow-up with the provider to be sent a few days later (provider-side
    /// scheduling; the delay is provider configuration). Returns the scheduled message's identifier so it can
    /// later be cancelled.</summary>
    Task<SentMessage> ScheduleFollowUpAsync(string toNumber, string body, CancellationToken ct);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken ct);

    /// <summary>Read the provider's current state for a message.</summary>
    Task<MessageState> FetchStateAsync(string messageSid, CancellationToken ct);

    /// <summary>Dispose the message body at the provider (redaction) so its text is no longer retrievable there.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken ct);

    /// <summary>List the provider's messages sent from the configured number within a date-sent window.</summary>
    Task<ProviderMessageList> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>The configured sending number, exposed for reporting/reconciliation display.</summary>
    string FromNumber { get; }
}
