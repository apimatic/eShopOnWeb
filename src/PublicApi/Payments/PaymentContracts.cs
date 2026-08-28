using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardRequest(
    [Required] string Name,
    [Required, CreditCard] string Number,
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] string Expiry,
    [Required, RegularExpression(@"^\d{3,4}$")] string SecurityCode,
    [Required] BillingAddressRequest BillingAddress);

public sealed record BillingAddressRequest(
    [Required] string AddressLine1,
    string? AddressLine2,
    [Required] string City,
    string? State,
    [Required] string PostalCode,
    [Required, RegularExpression(@"^[A-Za-z]{2}$")] string CountryCode);

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest([Required, StringLength(64, MinimumLength = 1)] string IdempotencyKey, decimal? Amount);

public sealed record PayPalAuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status,
    decimal Amount, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal Amount,
    decimal? Fee, decimal? Net, DateTimeOffset CreatedAt);
public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);
public sealed record PayPalVaultResult(string PaymentTokenId, string? CustomerId, string Brand,
    string LastFour, string Expiry);
public sealed record PayPalTransaction(string TransactionId, string? PayPalReferenceId, string? InvoiceId,
    string EventCode, string Status, decimal? Amount, string? Currency, decimal? Fee,
    DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt);

public sealed record PaymentView(string Status, string Currency, decimal Total,
    string? PayPalOrderId, string? AuthorizationId, string? AuthorizationStatus, decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt, string? CaptureId, string? CaptureStatus,
    decimal? CapturedAmount, decimal? PayPalFee, decimal? NetAmount, decimal RefundedAmount);

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, string FulfillmentStatus,
    IReadOnlyList<OrderItemView> Items, PaymentView Payment);
