using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>Outcome of asking the provider to validate a phone number.</summary>
public record NumberValidationResult
{
    /// <summary>True when the provider gave a definitive verdict (valid or invalid).</summary>
    public bool Success => FailureKind == MessagingFailureKind.None;
    public MessagingFailureKind FailureKind { get; init; } = MessagingFailureKind.None;
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical (E.164) form of the number. Store this, not the caller's input.</summary>
    public string? CanonicalNumber { get; init; }
    public string? NationalFormat { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
    public int? ProviderStatusCode { get; init; }
}
