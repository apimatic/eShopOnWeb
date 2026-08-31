using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>Outcome of listing the provider's messages for a range.</summary>
public record ListMessagesOutcome
{
    public bool Success => FailureKind == MessagingFailureKind.None;
    public MessagingFailureKind FailureKind { get; init; } = MessagingFailureKind.None;
    public IReadOnlyList<ProviderMessage> Messages { get; init; } = new List<ProviderMessage>();

    /// <summary>True when a page cap stopped enumeration before the provider signalled the end.</summary>
    public bool Truncated { get; init; }
    public int? ProviderStatusCode { get; init; }
}
