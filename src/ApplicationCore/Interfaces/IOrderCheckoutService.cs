using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<OrderDetailsDto> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress, CancellationToken cancellationToken = default);

    Task<OrderDetailsDto> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderDetailsDto> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderDetailsDto> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundDetailsDto> RefundAsync(string buyerId, int orderId, RefundOrderCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderDetailsDto>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PayOrderCommand
{
    public int? PaymentMethodId { get; init; }
    public CardPaymentCommand? Card { get; init; }
}

public sealed class CardPaymentCommand
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public BillingAddressCommand? BillingAddress { get; init; }
}

public sealed class BillingAddressCommand
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public sealed class RefundOrderCommand
{
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class OrderDetailsDto
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public IReadOnlyList<OrderItemDetailsDto> Items { get; init; } = Array.Empty<OrderItemDetailsDto>();
    public PaymentDetailsDto? Payment { get; init; }
}

public sealed class OrderItemDetailsDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

public sealed class PaymentDetailsDto
{
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalOrderStatus { get; init; }
    public string? InvoiceId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationCreatedAt { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? PayPalCaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal RemainingRefundableAmount { get; init; }
    public IReadOnlyList<RefundDetailsDto> Refunds { get; init; } = Array.Empty<RefundDetailsDto>();
}

public sealed class RefundDetailsDto
{
    public int RefundId { get; init; }
    public string PayPalRefundId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
