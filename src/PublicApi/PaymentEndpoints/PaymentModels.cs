using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- request DTOs ----

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress?.ToGatewayAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public GatewayAddress ToGatewayAddress() => new(AddressLine1, City, State, PostalCode, CountryCode);
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }

    [JsonIgnore] public string? BuyerId { get; set; }
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string? BuyerId { get; set; }
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string? BuyerId { get; set; }
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

/// <summary>Context-only request for operator/read endpoints that carry no JSON body.</summary>
public class OrderOperationRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public CancellationToken Cancellation { get; set; }
}

// ---- response DTOs ----

public class RefundDto
{
    public int Id { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public string? LastError { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentResponse From(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PaymentMethod = payment.PaymentMethodDescription,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        LastError = payment.LastError,
        Refunds = payment.Refunds
            .Select(r => new RefundDto { Id = r.Id, RefundId = r.PayPalRefundId, Amount = r.Amount, Status = r.Status })
            .ToList(),
    };
}
