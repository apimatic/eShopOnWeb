using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// Request bodies for the payment endpoints. Response shapes reuse the application service DTOs directly,
// except for the create/refund responses which surface the required top-level identifier.

public class CreateOrderApiRequest
{
    public List<OrderLineApi> Items { get; set; } = new();
    public ShippingAddressApi? ShipTo { get; set; }
}

public class OrderLineApi
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressApi
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>Card details for a one-off payment, or the id of a previously saved card. Supply exactly one.</summary>
public class PayApiRequest
{
    public CardApi? Card { get; set; }
    public int? SavedCardId { get; set; }
}

public class CardApi
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

public class RefundApiRequest
{
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodApiRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}
