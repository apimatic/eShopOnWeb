using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record AddressData(string Street, string City, string State, string Country, string ZipCode);

public record OrderItemInput(int CatalogItemId, int Quantity);

public record CardData(string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvc,
    string Name,
    AddressData? Address);

public record CreateOrderData(string BuyerId,
    IReadOnlyList<OrderItemInput> Items,
    AddressData? ShipToAddress);

public record PayOrderData(int OrderId,
    string BuyerId,
    CardData? Card,
    int? PaymentMethodId);

public record RefundOrderData(int OrderId,
    string BuyerId,
    bool IsAdministrator,
    long? AmountCents,
    string IdempotencyKey);

public record SavePaymentMethodData(string BuyerId, CardData Card);

public record OrderLineDto(int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Units,
    decimal LineTotal);

public record RefundDto(int RefundId,
    string TransferId,
    long AmountCents,
    decimal Amount,
    string State,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

public record PaymentStateDto(string Status,
    string? Currency,
    long? AuthorizedAmountCents,
    string? AuthorizationId,
    string? AuthorizationState,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureTransferId,
    string? CaptureState,
    long? CapturedAmountCents,
    long? FinixFeeCents,
    long? NetAmountCents,
    string? PaymentInstrumentId,
    int? PaymentMethodId,
    long RefundedAmountCents,
    long RefundableAmountCents,
    IReadOnlyList<RefundDto> Refunds);

public record OrderDto(int OrderId,
    string Status,
    decimal Total,
    long TotalCents,
    string Currency,
    DateTimeOffset OrderDate,
    AddressData ShipToAddress,
    IReadOnlyList<OrderLineDto> Items,
    PaymentStateDto Payment);

public record SavedPaymentMethodDto(int PaymentMethodId,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Name,
    DateTimeOffset CreatedAt);

public record FinixTransactionDto(string Id,
    string? Type,
    string? State,
    long? AmountCents,
    long? FeeCents,
    DateTimeOffset? CreatedAt,
    string? ParentTransferId,
    int? MatchedOrderId,
    string? MatchedRefundTransferId,
    bool Matched);

public record ReconciliationOrderDto(int OrderId,
    string Status,
    long TotalCents,
    string Currency,
    string? CaptureTransferId,
    DateTimeOffset OrderDate,
    bool Matched);

public record ReconciliationReport(DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    int FinixPagesFetched,
    int FinixTransactionCount,
    IReadOnlyList<FinixTransactionDto> FinixTransactions,
    IReadOnlyList<ReconciliationOrderDto> Orders,
    IReadOnlyList<FinixTransactionDto> UnmatchedFinixTransactions,
    IReadOnlyList<ReconciliationOrderDto> UnmatchedOrders);
