using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class SmsMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Body { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public sealed class SmsSendResult
{
    public required string Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class PhoneLookupResult
{
    public bool IsUsable { get; init; }
    public string? CanonicalNumber { get; init; }
    public string? RejectionReason { get; init; }
}

public sealed class SmsGatewayException : Exception
{
    public int? StatusCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
