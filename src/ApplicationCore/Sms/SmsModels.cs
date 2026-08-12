using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>Result of validating a phone number with the provider's Lookup service.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>Outcome of handing a message to the provider. <see cref="Sid"/> is the provider's identifier.</summary>
public record SmsSendResult(string Sid, string Status, int? ErrorCode);

/// <summary>The provider's current delivery outcome for a single message.</summary>
public record SmsStatusResult(string Status, int? ErrorCode);

/// <summary>One message as the provider records it, used for reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string Status, string? From, string? To, DateTimeOffset? DateSent, int? ErrorCode);

/// <summary>
/// Classification of provider message statuses. Only <c>delivered</c> (and <c>read</c> for
/// rich channels) means the handset received it; <c>sent</c> means only the carrier accepted it.
/// </summary>
public static class SmsStatuses
{
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";

    private static readonly HashSet<string> DeliveredStates = new(StringComparer.OrdinalIgnoreCase) { "delivered", "read" };
    private static readonly HashSet<string> FailedStates = new(StringComparer.OrdinalIgnoreCase) { "undelivered", "failed" };
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { "delivered", "read", "undelivered", "failed", "canceled" };

    public static bool IsDelivered(string? status) => status is not null && DeliveredStates.Contains(status);

    /// <summary>A message that did not reach the shopper — the state an operator resends against.</summary>
    public static bool IsFailure(string? status) => status is not null && FailedStates.Contains(status);

    public static bool IsTerminal(string? status) => status is not null && TerminalStates.Contains(status);
}
