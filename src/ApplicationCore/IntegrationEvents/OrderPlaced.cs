using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb order was created. UC2 uses this as the automatic
/// "one order placed → one billable unit" trigger (plan §8).
/// </summary>
/// <param name="BuyerId">The eShopOnWeb buyer identity, which is also the billing customer reference.</param>
public record OrderPlaced(string BuyerId, int OrderId) : INotification;
