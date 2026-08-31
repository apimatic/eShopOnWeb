using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>Outcome of asking the provider to validate a phone number.</summary>
public sealed record PhoneNumberValidationResult(
    bool IsValid,
    string? CanonicalNumber,
    string? ValidationError);

/// <summary>Outcome of a send/schedule/cancel/redact operation against the provider.</summary>
public sealed record SmsSendResult(
    bool Success,
    string? MessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    string? ErrorMessage);

/// <summary>The provider's current view of a single message.</summary>
public sealed record ProviderMessageState(
    string MessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    DateTimeOffset? DateSent);

/// <summary>A message as listed by the provider for reconciliation.</summary>
public sealed record ProviderMessageSummary(
    string MessageSid,
    string? To,
    string? From,
    string? Status,
    int? ErrorCode,
    DateTimeOffset? DateSent);
