using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public interface ISmsGateway
{
    Task<SmsMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    Task<SmsMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<SmsMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessage> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default);
}
