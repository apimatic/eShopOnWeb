using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool IsValid,
    string? CanonicalPhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public interface IPhoneNumberLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public record TwilioMessageSnapshot(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateCreated,
    string? DateSent);

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<TwilioMessageSnapshot?> SendSmsAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
