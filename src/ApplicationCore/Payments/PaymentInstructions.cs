using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of an order to place: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>The shipping address for an order (all fields optional; a default is used when omitted).</summary>
public record ShippingAddressInput(
    string? Street = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? ZipCode = null);

/// <summary>
/// How to pay an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstruction(RawCard? Card = null, int? SavedPaymentMethodId = null);

/// <summary>Details for saving a card to the vault.</summary>
public record SaveCardInstruction(RawCard Card, string? Alias = null);
