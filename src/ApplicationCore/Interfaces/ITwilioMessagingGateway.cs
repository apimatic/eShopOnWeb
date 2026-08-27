using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingGateway
{
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string submittedNumber, CancellationToken cancellationToken);
    Task<ProviderMessageResult> SendMessageAsync(string destination, string body, DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> DisposeMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageSnapshot>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessageResult(bool Accepted, ProviderMessageSnapshot? Message, string FailureStatus);

public sealed record ProviderMessageSnapshot(
    string ProviderMessageSid,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated,
    string? Direction);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    public int? StatusCode { get; }
}
