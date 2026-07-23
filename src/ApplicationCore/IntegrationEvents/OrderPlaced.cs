using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a shopper completed an eShopOnWeb checkout. The subscription module listens
/// for this to bill one pay-as-you-go unit (UC2's automatic trigger).
/// </summary>
/// <remarks>
/// Order creation is <em>not</em> conditional on any handler succeeding: the order is already
/// persisted before this is published, and both the publish and every handler swallow their own
/// failures, so billing can never block or roll back eShopOnWeb's order lifecycle.
/// </remarks>
/// <param name="BuyerId">The eShopOnWeb user reference that placed the order.</param>
/// <param name="OrderId">The identifier of the order that was created.</param>
public record OrderPlaced(string BuyerId, int OrderId) : INotification;
