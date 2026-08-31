using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreatePaidOrderRequest
{
    public IReadOnlyList<CreateOrderItemRequest> Items { get; init; } = Array.Empty<CreateOrderItemRequest>();
    public required ShippingAddressRequest ShipToAddress { get; init; }
}

public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressRequest(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record CreatePaidOrderResponse(int OrderId, string Status, decimal Total, string Currency);

public class CardRequest
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public required BillingAddressRequest BillingAddress { get; init; }

    public override string ToString() => "[REDACTED CARD DETAILS]";
}

public sealed record BillingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed record PayOrderResponse(
    int OrderId,
    string Status,
    PaymentDto Payment);

public sealed record FulfilOrderResponse(
    int OrderId,
    string Status,
    PaymentDto Payment);

public sealed record CancelOrderResponse(int OrderId, string Status, PaymentDto? Payment);

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record RefundOrderResponse(
    string RefundId,
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    decimal TotalRefunded,
    decimal RefundableRemaining);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);

public sealed record OrderItemDto(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public sealed record PaymentDto(
    string Status,
    string Currency,
    string? PayPalOrderId,
    string? PayPalOrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? CardBrand,
    string? CardLast4,
    IReadOnlyList<RefundDto> Refunds);

public sealed record RefundDto(
    string? RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed class SavePaymentMethodRequest : CardRequest
{
}

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string Expiry,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationTransactionDto> PayPalTransactions,
    IReadOnlyList<UnmatchedEShopPaymentDto> EShopPaymentsWithoutPayPalRecord);

public sealed record ReconciliationTransactionDto(
    string TransactionId,
    string? ReferenceId,
    string EventCode,
    string Status,
    DateTimeOffset InitiatedAt,
    decimal Amount,
    decimal? Fee,
    string Currency,
    int? OrderId,
    string MatchStatus);

public sealed record UnmatchedEShopPaymentDto(
    int OrderId,
    string Operation,
    string PayPalId,
    string Status,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Currency);

internal static class PaymentMappings
{
    public static PaymentDto ToDto(this OrderPayment payment) => new(
        payment.Status.ToString(),
        payment.Currency,
        payment.PayPalOrderId,
        payment.PayPalOrderStatus,
        payment.AuthorizationId,
        payment.AuthorizationStatus,
        payment.AuthorizationAmount,
        payment.AuthorizationExpiresAt,
        payment.CaptureId,
        payment.CaptureStatus,
        payment.CaptureAmount,
        payment.PayPalFee,
        payment.NetAmount,
        payment.RefundedAmount,
        payment.CardBrand,
        payment.CardLast4,
        payment.Refunds
            .OrderBy(x => x.CreatedAt)
            .Select(x => new RefundDto(x.PayPalRefundId, x.PayPalStatus ?? x.Status.ToString(), x.Amount,
                x.Currency, x.CreatedAt))
            .ToArray());
}
