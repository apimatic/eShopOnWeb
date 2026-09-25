using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by the shopper for a one-off payment or to vault. Never stored or logged by the app.</summary>
public class CardApiModel
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // ISO-8601 YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCountryCode { get; set; } // 2-letter ISO
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, CardholderName,
        BillingStreet, BillingCity, BillingState, BillingCountryCode, BillingPostalCode);
}

public class OrderLineApiModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressApiModel
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    public List<OrderLineApiModel> Items { get; set; } = new();
    public ShippingAddressApiModel? ShippingAddress { get; set; }
}

/// <summary>Response to placing an order — carries the new order id as a top-level field.</summary>
public record PlaceOrderResponse(int OrderId, string State);

public class PayOrderRequest
{
    public int OrderId { get; set; } // bound from the route
    /// <summary>Pay with a previously saved card; mutually exclusive with <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
    /// <summary>Pay with a one-off card; mutually exclusive with <see cref="PaymentMethodId"/>.</summary>
    public CardApiModel? Card { get; set; }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; } // bound from the route
    /// <summary>Amount to refund; omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key: the same key never refunds twice; two keys are two refunds.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardApiModel Card { get; set; } = new();
}

/// <summary>An operator action on an order identified only by its route id.</summary>
public class OrderActionRequest
{
    public int OrderId { get; set; }
}

/// <summary>Reconciliation range (ISO-8601 date-times), bound from the query string.</summary>
public class ReconciliationRequest
{
    public System.DateTimeOffset From { get; set; }
    public System.DateTimeOffset To { get; set; }
}

/// <summary>Marker request for endpoints whose only input is the caller's identity.</summary>
public class EmptyRequest { }

/// <summary>Resolves the caller's identity (buyer id) from the JWT, tolerating the usual name-claim mappings.</summary>
public static class CallerIdentity
{
    public static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue("unique_name")
        ?? user.FindFirstValue("name")
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new PaymentException(401, "The caller identity could not be determined from the token.");
}
