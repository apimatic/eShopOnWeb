using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PostalAddressRequest
{
    [Required] public string Street { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    [Required] public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    [Required] public string Country { get; init; } = string.Empty;
    [Required] public string ZipCode { get; init; } = string.Empty;
}

public sealed record OrderLineRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; init; }
    [Range(1, 1000)] public int Quantity { get; init; }
}

public sealed record CreateOrderRequest
{
    [Required, MinLength(1)] public IReadOnlyList<OrderLineRequest> Items { get; init; } = [];
    [Required] public PostalAddressRequest ShippingAddress { get; init; } = new();
}

public sealed record CreateOrderResponse(int OrderId, decimal Total, string Currency, string PaymentStatus);

public sealed record CardRequest
{
    [Required, RegularExpression("^[0-9]{13,19}$")] public string Number { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; init; } = string.Empty;
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public PostalAddressRequest BillingAddress { get; init; } = new();
}

public sealed record PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed record RefundOrderRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string IdempotencyKey { get; init; } = string.Empty;
    [Range(typeof(decimal), "0.01", "9999999999999999")] public decimal? Amount { get; init; }
}

public sealed record SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; init; } = new();
}

public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string? Expiry);

public sealed record RefundResponse(int RefundId, string? ProviderRefundId, decimal Amount, string Status);

public sealed record PaymentResponse(
    int OrderId,
    string PaymentStatus,
    string? AuthorizationId,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public sealed record OrderLineResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string FulfillmentStatus,
    PaymentResponse? Payment,
    IReadOnlyList<OrderLineResponse> Items,
    IReadOnlyList<RefundResponse> Refunds);

public sealed record ReconciliationTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Status,
    string? InvoiceId,
    int? OrderId,
    string MatchState);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationTransaction> PayPalTransactions,
    IReadOnlyList<int> EShopOrderIdsMissingAtPayPal);
