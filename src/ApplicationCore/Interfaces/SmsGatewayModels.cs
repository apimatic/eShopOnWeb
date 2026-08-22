using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalE164 { get; init; }
    public string? NationalFormat { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public class SmsSendRequest
{
    public required string ToE164 { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class SmsMessageSnapshot
{
    public string? Sid { get; init; }
    public string Status { get; init; } = "unknown";
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public class SmsSendResult
{
    public bool Accepted { get; init; }
    public SmsMessageSnapshot? Message { get; init; }
    public string? FailureStatus { get; init; }
    public int? ErrorCode { get; init; }
}
