using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>A requested order line: how many of a catalog item to buy.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The result of a refund request, including whether it was a replay of an earlier request under the same key.</summary>
public record RefundOutcome(PaymentRefund Refund, bool WasReplay);
