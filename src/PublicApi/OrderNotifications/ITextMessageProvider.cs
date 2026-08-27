using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public interface ITextMessageProvider
{
    Task<string?> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> SendAsync(string destination, string body, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> ScheduleAsync(string destination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessageSnapshot> RedactAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ProviderMessageSnapshot(
    string? Sid,
    string? Status,
    string? Direction,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateCreated,
    string? DateSent,
    string? DateUpdated);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string safeMessage, int? statusCode = null, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
