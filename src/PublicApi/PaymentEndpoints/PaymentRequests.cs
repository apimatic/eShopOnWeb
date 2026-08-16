using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request bodies for the payment endpoints ----

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in "YYYY-MM" form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequest
{
    /// <summary>Amount to refund; when omitted the full remaining captured amount is refunded.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key so repeating the request under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Alias { get; set; }
}

/// <summary>Maps request DTOs to the application-layer input records and reads the caller's identity.</summary>
public static class RequestMapper
{
    /// <summary>The shopper/operator identity carried by the JWT (its name claim).</summary>
    public static string? BuyerId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static PayPalCardDetails ToCardDetails(CardRequest card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.Name,
        card.BillingAddress is null ? null : new PayPalBillingAddress(
            card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2,
            card.BillingAddress.AdminArea1,
            card.BillingAddress.AdminArea2,
            card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));

    public static PlaceOrderInput ToPlaceOrderInput(CreateOrderRequest request)
    {
        var items = new List<OrderLineInput>();
        foreach (var line in request.Items)
            items.Add(new OrderLineInput(line.CatalogItemId, line.Quantity));

        ShippingAddressInput? shipTo = request.ShipTo is null
            ? null
            : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
                request.ShipTo.Country, request.ShipTo.ZipCode);

        return new PlaceOrderInput(items, shipTo);
    }

    public static PayOrderInput ToPayOrderInput(PayOrderRequest request) => new(
        request.Card is null ? null : ToCardDetails(request.Card),
        request.SavedPaymentMethodId);
}
