namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's current record of what became of a single message.
/// </summary>
public sealed record MessageDeliveryInfo(string? Status, int? ErrorCode, string? ErrorMessage);
