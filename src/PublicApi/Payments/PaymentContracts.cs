using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record BillingAddressRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CardRequest(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressRequest BillingAddress);

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressRequest(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record PlaceOrderRequest(
    IReadOnlyList<OrderLineRequest> Items,
    ShippingAddressRequest ShippingAddress);

public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);

public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey, string? Note);

public sealed record SavePaymentMethodRequest(CardRequest Card);

public sealed record OrderCreatedResponse(int OrderId, string Status, decimal Total, string Currency);

public sealed record AuthorizationResponse(
    int OrderId,
    string Status,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public sealed record CaptureResponse(
    int OrderId,
    string Status,
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    string Currency);

public sealed record CancellationResponse(int OrderId, string Status, string AuthorizationStatus);

public sealed record RefundResponse(
    string RefundId,
    int OrderId,
    string Status,
    string RefundStatus,
    decimal RefundedAmount,
    decimal TotalRefunded,
    decimal RemainingRefundable,
    string Currency);

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string LastFour,
    string Expiry,
    DateTimeOffset CreatedAt);

public sealed record RefundSummary(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);

public sealed record OrderSummaryResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string? Currency,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal TotalRefunded,
    IReadOnlyList<RefundSummary> Refunds);

public sealed record ReconciliationEntry(
    string Source,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? TransactionStatus,
    DateTimeOffset? TransactionDate,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    int? OrderId,
    string? LocalPaymentType,
    string MatchStatus);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Entries);

internal sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

internal sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net);

internal sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency);

internal sealed record PayPalVaultResult(
    string TokenId,
    string? CustomerId,
    string Brand,
    string LastFour,
    string Expiry);

internal sealed record PayPalTransaction(
    string Id,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? Date,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? InvoiceId,
    string? CustomField);
