using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SmsSendRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class SmsMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
}

public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, int? statusCode = null, int? providerCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int? StatusCode { get; }
    public int? ProviderCode { get; }
}

public interface ISmsMessageGateway
{
    string ConfiguredFromNumber { get; }
    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentByConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
