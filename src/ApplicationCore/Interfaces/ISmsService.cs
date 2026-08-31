using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio). Send/schedule/cancel/fetch operations
/// report failure as an outcome on the result rather than throwing, so a message that
/// cannot be sent never fails the underlying business operation. Validation and listing
/// throw SmsProviderException on provider faults, since their callers need to distinguish
/// "provider said no" from "provider unreachable".
/// </summary>
public interface ISmsService
{
    /// <summary>Validates a number with the provider and returns its canonical form.</summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Redacts a message's body at the provider; the message record and its outcome survive.</summary>
    Task<SmsSendResult> RedactBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Fetches the provider's current record of a single message.</summary>
    Task<SmsSendResult> FetchAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Lists the provider's own records of messages sent from this application's
    /// configured sending number within a date-sent window.</summary>
    Task<IReadOnlyList<ProviderSmsMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
