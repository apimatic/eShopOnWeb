using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Result of validating/canonicalizing a number with the provider's Lookup.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>What the provider returned when a message was created (sent or scheduled).</summary>
public record SentMessage(string? Sid, string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent);

/// <summary>The provider's current view of a single message, from a fetch.</summary>
public record MessageState(string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent);

/// <summary>One message as the provider reports it during reconciliation.</summary>
public record ProviderMessage(string? Sid, string? Status, string? From, string? To, DateTimeOffset? DateSent, int? ErrorCode);

/// <summary>The provider's messages for a window, plus whether the page walk was cut short.</summary>
public record ProviderMessageList(IReadOnlyList<ProviderMessage> Messages, bool Truncated, int PagesFetched);
