using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool isValid, string? canonicalNumber)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
    }

    public bool IsValid { get; }
    public string? CanonicalNumber { get; }
}

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ProviderMessage
{
    public ProviderMessage(
        string sid,
        string? status,
        int? errorCode,
        string? errorMessage,
        string? from,
        string? dateSent,
        string? dateCreated,
        string? body)
    {
        Sid = sid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        From = from;
        DateSent = dateSent;
        DateCreated = dateCreated;
        Body = body;
    }

    public string Sid { get; }
    public string? Status { get; }
    public int? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public string? From { get; }
    public string? DateSent { get; }
    public string? DateCreated { get; }
    public string? Body { get; }
}
