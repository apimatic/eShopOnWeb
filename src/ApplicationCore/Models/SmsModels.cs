using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? NationalFormat { get; set; }
    public string? ValidationError { get; set; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber, string? nationalFormat) =>
        new() { IsValid = true, CanonicalNumber = canonicalNumber, NationalFormat = nationalFormat };

    public static PhoneNumberValidationResult Invalid(string? error) =>
        new() { IsValid = false, ValidationError = error };
}

public class SmsSendResult
{
    public bool Accepted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static SmsSendResult Success(string sid, string? status) =>
        new() { Accepted = true, ProviderMessageSid = sid, Status = status };

    public static SmsSendResult Failure(string? errorMessage, int? errorCode = null) =>
        new() { Accepted = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>The provider's own record of a single message.</summary>
public class SmsMessageState
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateUpdated { get; set; }
}
