using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The provider's verdict on a candidate number: whether it is a well-formed, assignable number and, if so,
/// its canonical E.164 form. This is a numbering-plan judgement, not a live-reachability one — a valid number
/// may still be legitimately undeliverable for the account.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? E164, IReadOnlyList<string> Errors);

/// <summary>The outcome of handing a message to the provider (immediate or scheduled).</summary>
public record MessageDispatchResult(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? ScheduledAt);

/// <summary>A later read of the provider's authoritative state for one message.</summary>
public record MessageState(string Sid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it, used to line the provider's ledger up against eShop's.</summary>
public record ProviderMessage(
    string? Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
