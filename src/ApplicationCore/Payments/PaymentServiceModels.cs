using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw <see cref="Card"/> details for a one-off payment, or the id of one
/// of the shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedCardId);

/// <summary>An order paired with its payment record (payment may be null while awaiting payment).</summary>
public record OrderWithPayment(Order Order, Payment? Payment);
