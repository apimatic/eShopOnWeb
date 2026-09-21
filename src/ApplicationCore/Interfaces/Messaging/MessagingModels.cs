using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>Outcome of asking the provider whether a raw number is a usable destination.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>Result of creating (or scheduling) a message at the provider.</summary>
public record MessageSendResult(
    string Sid,
    string? Status,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>The provider's current state for a previously created message.</summary>
public record MessageDeliveryState(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent);

/// <summary>A message as the provider reports it in a reconciliation listing.</summary>
public record ProviderMessageRecord(
    string? Sid,
    string? Status,
    string? To,
    string? From,
    string? DateSent);

/// <summary>
/// A provider listing over a range. <see cref="Truncated"/> is true when a safety page cap was hit before
/// the provider signalled the end, so the caller can report the result as partial rather than complete.
/// </summary>
public record ProviderMessageListing(IReadOnlyList<ProviderMessageRecord> Messages, bool Truncated);
