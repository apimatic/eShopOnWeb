using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixAddress
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "USA";
}

public sealed class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinixAddress? Address { get; set; }
}

public sealed class FinixPaymentInstrument
{
    public string Id { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
}

public sealed class FinixAuthorization
{
    public string Id { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? TransferId { get; set; }
    public bool? IsVoid { get; set; }
    public string? VoidState { get; set; }
    public long? Amount { get; set; }
    public long? AmountRequested { get; set; }
    public string? Currency { get; set; }
    public string? Source { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Merchant { get; set; }
}

public sealed class FinixTransfer
{
    public string Id { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public long? Amount { get; set; }
    public long? AmountRequested { get; set; }
    public string? Currency { get; set; }
    public string? Source { get; set; }
    public string? ParentTransfer { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? Fee { get; set; }
    public string? Merchant { get; set; }
    public Dictionary<string, object?>? Tags { get; set; }
}

public sealed class FinixFee
{
    public string Id { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeType { get; set; }
    public string? FeeSubtype { get; set; }
    public string? LinkedTo { get; set; }
    public string? Category { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string? code, string message, string? rawBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        RawBody = rawBody;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? RawBody { get; }
}
