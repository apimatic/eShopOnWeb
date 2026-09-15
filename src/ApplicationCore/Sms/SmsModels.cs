using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's answer to "is this a usable destination, and what is its canonical form?".
/// </summary>
public class PhoneNumberLookupResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical E.164 form of the number (present when valid).</summary>
    public string? CanonicalNumber { get; init; }

    /// <summary>Reasons the provider gave for rejecting the number, when invalid.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The outcome of asking the provider to create (send or schedule) a message.
/// A non-null <see cref="Sid"/> means the provider accepted the create — not that a handset received it.
/// </summary>
public class SmsSendResult
{
    public bool Accepted { get; init; }
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? SentAt { get; init; }
}

/// <summary>
/// A message as the provider records it, used both to refresh a known message's delivery outcome
/// and to reconcile the provider's own list against what eShop believes it sent.
/// </summary>
public class ProviderMessage
{
    public string Sid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
