namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The provider's own record of a message, as returned by its message listing.
/// Used by reconciliation to line the provider's view up against what eShop believes it sent.
/// </summary>
public record ProviderMessageRecord
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
