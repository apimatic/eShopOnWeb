using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? Zip { get; set; }
    public string? PostalCode { get; set; }

    public ShippingAddressInput ToInput() => new(Street, City, State, Country, ZipCode ?? Zip ?? PostalCode);
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShippingAddress { get; set; }

    public CreateOrderInput ToInput() => new(
        Items.Select(item => new OrderLineInput(item.CatalogItemId, item.Quantity)).ToList(),
        ShippingAddress?.ToInput());
}

public class CardDetailsDto
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public ShippingAddressDto? BillingAddress { get; set; }

    public CardInput ToInput() => new(
        Number ?? CardNumber ?? string.Empty,
        ExpirationMonth ?? ExpMonth ?? 0,
        ExpirationYear ?? ExpYear ?? 0,
        SecurityCode ?? Cvv,
        Name ?? CardholderName,
        BillingAddress?.ToInput());
}

public class PayOrderRequest : BaseRequest
{
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedCardId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public int? CardId { get; set; }

    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public ShippingAddressDto? BillingAddress { get; set; }

    public CardInput? ResolveCard()
    {
        if (Card is not null)
        {
            return Card.ToInput();
        }

        if (string.IsNullOrWhiteSpace(Number)
            && string.IsNullOrWhiteSpace(CardNumber)
            && ExpirationMonth is null
            && ExpMonth is null
            && ExpirationYear is null
            && ExpYear is null
            && string.IsNullOrWhiteSpace(Cvv)
            && string.IsNullOrWhiteSpace(SecurityCode)
            && string.IsNullOrWhiteSpace(Name)
            && string.IsNullOrWhiteSpace(CardholderName)
            && BillingAddress is null)
        {
            return null;
        }

        return new CardInput(
            Number ?? CardNumber ?? string.Empty,
            ExpirationMonth ?? ExpMonth ?? 0,
            ExpirationYear ?? ExpYear ?? 0,
            SecurityCode ?? Cvv,
            Name ?? CardholderName,
            BillingAddress?.ToInput());
    }

    public int? ResolvePaymentMethodId() => PaymentMethodId ?? SavedCardId ?? SavedPaymentMethodId ?? CardId;
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto? Card { get; set; }

    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public ShippingAddressDto? BillingAddress { get; set; }

    public CardInput ResolveCard()
    {
        if (Card is not null)
        {
            return Card.ToInput();
        }

        return new CardInput(
            Number ?? CardNumber ?? string.Empty,
            ExpirationMonth ?? ExpMonth ?? 0,
            ExpirationYear ?? ExpYear ?? 0,
            SecurityCode ?? Cvv,
            Name ?? CardholderName,
            BillingAddress?.ToInput());
    }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKeySnake { get; set; }

    public RefundOrderInput ToInput() => new(
        AmountCents,
        Amount,
        IdempotencyKey ?? IdempotencyKeySnake ?? string.Empty);
}

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
}

public class MyOrdersRequest : BaseRequest
{
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? FinixState { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? FinixAuthorizationId { get; set; }
    public string? FinixAuthorizationState { get; set; }
    public string? FinixCaptureTransferId { get; set; }
    public string? FinixCaptureState { get; set; }
    public string? FinixPaymentInstrumentId { get; set; }
    public bool UsedSavedCard { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? FinixFeeCents { get; set; }
    public decimal? FinixFee { get; set; }
    public long? NetAmountCents { get; set; }
    public decimal? NetAmount { get; set; }
    public long RefundableAmountCents { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FinixTransactionDto
{
    public string RecordType { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public long? AmountCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? ParentTransferId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public bool Matched { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedRefundId { get; set; }
}

public class ReconciliationMismatchDto
{
    public string RecordType { get; set; } = string.Empty;
    public string? FinixId { get; set; }
    public int? OrderId { get; set; }
    public int? RefundId { get; set; }
    public long? AmountCents { get; set; }
    public string? Status { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
    public OrderDto? Order { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
    public OrderDto? Order { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
    public OrderDto? Order { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public RefundDto? Refund { get; set; }
    public PaymentDto? Payment { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto? PaymentMethod { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Removed { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int EshopPaymentCount { get; set; }
    public List<FinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationMismatchDto> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationMismatchDto> UnmatchedEshopPayments { get; set; } = new();
}

internal static class PaymentDtoMapper
{
    public static OrderDto ToDto(OrderView view)
    {
        return new OrderDto
        {
            OrderId = view.OrderId,
            BuyerId = view.BuyerId,
            OrderDate = view.OrderDate,
            Total = view.Total,
            Currency = view.Currency,
            Status = view.Payment?.Status ?? "AwaitingPayment",
            Items = view.Items.Select(item => new OrderItemDto
            {
                CatalogItemId = item.CatalogItemId,
                ProductName = item.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                LineTotal = item.LineTotal
            }).ToList(),
            Payment = view.Payment is null ? null : ToDto(view.Payment)
        };
    }

    public static PaymentDto ToDto(PaymentView view)
    {
        return new PaymentDto
        {
            PaymentId = view.PaymentId,
            Status = view.Status,
            AmountCents = view.AmountCents,
            Amount = view.AmountCents / 100m,
            Currency = view.Currency,
            FinixAuthorizationId = view.FinixAuthorizationId,
            FinixAuthorizationState = view.FinixAuthorizationState,
            FinixCaptureTransferId = view.FinixCaptureTransferId,
            FinixCaptureState = view.FinixCaptureState,
            FinixPaymentInstrumentId = view.FinixPaymentInstrumentId,
            UsedSavedCard = view.UsedSavedCard,
            CapturedAmountCents = view.CapturedAmountCents,
            CapturedAmount = view.CapturedAmountCents is long captured ? captured / 100m : null,
            FinixFeeCents = view.FinixFeeCents,
            FinixFee = view.FinixFeeCents is long fee ? fee / 100m : null,
            NetAmountCents = view.NetAmountCents,
            NetAmount = view.NetAmountCents is long net ? net / 100m : null,
            RefundableAmountCents = view.RefundableAmountCents,
            RefundableAmount = view.RefundableAmountCents / 100m,
            FailureCode = view.FailureCode,
            FailureMessage = view.FailureMessage,
            AuthorizedAt = view.AuthorizedAt,
            CapturedAt = view.CapturedAt,
            FulfilledAt = view.FulfilledAt,
            CanceledAt = view.CanceledAt,
            Refunds = view.Refunds.Select(ToDto).ToList()
        };
    }

    public static RefundDto ToDto(RefundView view)
    {
        return new RefundDto
        {
            RefundId = view.RefundId,
            AmountCents = view.AmountCents,
            Amount = view.AmountCents / 100m,
            IdempotencyKey = view.IdempotencyKey,
            FinixTransferId = view.FinixTransferId,
            FinixState = view.FinixState,
            CreatedAt = view.CreatedAt
        };
    }

    public static SavedCardDto ToDto(SavedCardView view)
    {
        return new SavedCardDto
        {
            PaymentMethodId = view.PaymentMethodId,
            Brand = view.Brand,
            LastFour = view.LastFour,
            ExpirationMonth = view.ExpirationMonth,
            ExpirationYear = view.ExpirationYear,
            CreatedAt = view.CreatedAt
        };
    }

    public static FinixTransactionDto ToDto(FinixTransactionView view)
    {
        return new FinixTransactionDto
        {
            RecordType = view.RecordType,
            Id = view.Id,
            AmountCents = view.AmountCents,
            Currency = view.Currency,
            State = view.State,
            Type = view.Type,
            ParentTransferId = view.ParentTransferId,
            CreatedAt = view.CreatedAt,
            Matched = view.Matched,
            MatchedOrderId = view.MatchedOrderId,
            MatchedRefundId = view.MatchedRefundId
        };
    }

    public static ReconciliationMismatchDto ToDto(ReconciliationMismatch view)
    {
        return new ReconciliationMismatchDto
        {
            RecordType = view.RecordType,
            FinixId = view.FinixId,
            OrderId = view.OrderId,
            RefundId = view.RefundId,
            AmountCents = view.AmountCents,
            Status = view.Status,
            Reason = view.Reason
        };
    }
}

internal static class ClaimsPrincipalPaymentExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var claim = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(claim))
        {
            return claim;
        }

        throw new PaymentOperationException("missing_buyer", "The caller identity is missing from the token.", 401);
    }

    public static bool IsOperator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
