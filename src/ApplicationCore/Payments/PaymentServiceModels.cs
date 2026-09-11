using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>An order together with its payment state, for shopper-facing listings.</summary>
public record OrderWithPayment(Order Order, OrderPayment Payment);

/// <summary>How the shopper is paying: either raw card details, or a saved card id.</summary>
public record PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}
