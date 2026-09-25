using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A catalog item and quantity for placing an order.</summary>
public record OrderLineInput
{
    public required int CatalogItemId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>Shipping address for a placed order.</summary>
public record ShippingAddressInput
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public string? State { get; init; }
    public required string Country { get; init; }
    public required string ZipCode { get; init; }
}

/// <summary>
/// How to pay: either one-off <see cref="Card"/> details, or a saved card by
/// <see cref="SavedPaymentMethodId"/>. Exactly one must be provided.
/// </summary>
public record PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}
