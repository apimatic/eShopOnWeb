using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- shared input shapes -----------------------------------------------------------------------

public record OrderLineDto(int CatalogItemId, int Quantity);

public record AddressDto(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Card details for a one-off payment or to vault. Never stored or logged.</summary>
public record CardDto(
    string Number,
    string Expiry,           // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    BillingAddressDto? BillingAddress);

public record BillingAddressDto(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

// ---- POST /api/orders --------------------------------------------------------------------------

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

// ---- POST /api/orders/{orderId}/pay ------------------------------------------------------------

public class PayOrderRequest
{
    public int OrderId { get; set; } // set from route
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

// ---- POST /api/orders/{orderId}/fulfil | /cancel -----------------------------------------------

public record OrderActionRequest(int OrderId);

// ---- POST /api/orders/{orderId}/refunds --------------------------------------------------------

public class RefundOrderRequest
{
    public int OrderId { get; set; } // set from route
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public OrderPaymentView Order { get; set; } = null!;
}

// ---- GET /api/reconciliation -------------------------------------------------------------------

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

// ---- POST /api/payment-methods -----------------------------------------------------------------

public class CreatePaymentMethodRequest
{
    public CardDto Card { get; set; } = null!;
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
}

// ---- DELETE /api/payment-methods/{paymentMethodId} ---------------------------------------------

public record DeletePaymentMethodRequest(int PaymentMethodId);

// ---- mapping -----------------------------------------------------------------------------------

public static class PaymentMappings
{
    public static CardDetails ToCardDetails(CardDto dto) => new(
        dto.Number,
        dto.Expiry,
        dto.SecurityCode,
        dto.CardholderName,
        dto.BillingAddress is null
            ? null
            : new CardBillingAddress(
                dto.BillingAddress.AddressLine1,
                dto.BillingAddress.AddressLine2,
                dto.BillingAddress.City,
                dto.BillingAddress.State,
                dto.BillingAddress.PostalCode,
                dto.BillingAddress.CountryCode));

    public static Address? ToAddress(AddressDto? dto) =>
        dto is null ? null : new Address(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);
}
