using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? Expiration { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public sealed class PayOrderRequest : CardRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest : CardRequest
{
}

public sealed class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class RefundOrderRequest
{
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class RefundResponse
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentResponse
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountCents { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long AuthorizedAmountCents { get; set; }

    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FinixFeeCents { get; set; }
    public long NetProceedsCents { get; set; }

    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }

    public List<RefundResponse> Refunds { get; set; } = new();
}

public sealed class OrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
}

public sealed class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public sealed class PayOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public sealed class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public sealed class CancelOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public sealed class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public PaymentResponse Payment { get; set; } = new();
}

public sealed class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public sealed class SavedCardResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentMethodsResponse
{
    public List<SavedCardResponse> PaymentMethods { get; set; } = new();
}

public sealed class DeletePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Status { get; set; } = "Deleted";
}

public sealed class ReconciliationFinixTransactionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public long? FeeCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
}

public sealed class ReconciliationOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? FinixAuthorizationId { get; set; }
    public string? FinixCaptureTransferId { get; set; }
    public List<string> FinixRefundTransferIds { get; set; } = new();
    public bool Matched { get; set; }
}

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationFinixTransactionResponse> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderResponse> EShopOrders { get; set; } = new();
    public List<string> UnmatchedFinixTransactionIds { get; set; } = new();
    public List<int> UnmatchedOrderIds { get; set; } = new();
}

public static class PaymentDtoMapper
{
    public static OrderResponse ToResponse(Order order)
    {
        var payment = order.Payment;
        return new OrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            TotalCents = payment?.AmountCents ?? ToCents(order.Total()),
            Currency = payment?.Currency ?? string.Empty,
            Items = order.OrderItems.Select(item => new OrderItemResponse
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            }).ToList(),
            Payment = payment is null ? null : ToResponse(payment)
        };
    }

    public static PaymentResponse ToResponse(OrderPayment payment)
    {
        return new PaymentResponse
        {
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            AmountCents = payment.AmountCents,
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizedAmountCents = payment.AuthorizedAmountCents,
            CaptureTransferId = payment.FinixCaptureTransferId,
            CaptureState = payment.CaptureState,
            CapturedAmountCents = payment.CapturedAmountCents,
            FinixFeeCents = payment.FinixFeeCents,
            NetProceedsCents = payment.NetProceedsCents,
            RefundedAmountCents = payment.RefundedAmountCents,
            RefundableAmountCents = payment.RefundableAmountCents(),
            PaymentInstrumentId = payment.PaymentInstrumentId,
            LastError = payment.LastError,
            CreatedAt = payment.CreatedAt,
            AuthorizedAt = payment.AuthorizedAt,
            CapturedAt = payment.CapturedAt,
            CanceledAt = payment.CanceledAt,
            LastUpdatedAt = payment.LastUpdatedAt,
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RefundResponse
                {
                    RefundId = r.Id,
                    FinixRefundId = r.FinixTransferId,
                    AmountCents = r.AmountCents,
                    State = r.State,
                    CreatedAt = r.CreatedAt
                })
                .ToList()
        };
    }

    public static SavedCardResponse ToResponse(SavedCard card)
    {
        return new SavedCardResponse
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Name = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }

    public static SavePaymentMethodResponse ToSaveResponse(SavedCard card)
    {
        return new SavePaymentMethodResponse
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Name = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }

    public static CardDetails ToCardDetails(CardRequest request)
    {
        return new CardDetails
        {
            Number = request.Number ?? string.Empty,
            Expiration = request.Expiration,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            SecurityCode = request.SecurityCode ?? request.Cvc,
            Name = request.Name ?? string.Empty,
            BillingAddress = request.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    Line1 = request.BillingAddress.Line1,
                    Line2 = request.BillingAddress.Line2,
                    City = request.BillingAddress.City,
                    Region = request.BillingAddress.Region,
                    PostalCode = request.BillingAddress.PostalCode,
                    Country = request.BillingAddress.Country
                }
        };
    }

    public static Address? ToAddress(ShippingAddressRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        return new Address(
            request.Street ?? "N/A",
            request.City ?? "N/A",
            request.State ?? "NA",
            request.Country ?? "USA",
            request.ZipCode ?? "00000");
    }

    public static long ToCents(decimal amount)
    {
        return checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
