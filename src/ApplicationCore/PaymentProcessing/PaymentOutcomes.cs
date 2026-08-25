using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>Result of attempting to authorize an order's payment.</summary>
public abstract record AuthorizePaymentOutcome;

/// <summary>The order total was successfully held (authorized).</summary>
public sealed record AuthorizePaymentAuthorized(
    string PayPalOrderId, string AuthorizationId, string AuthorizationStatus,
    decimal AuthorizedAmount, string Currency, DateTimeOffset? ExpiresAt) : AuthorizePaymentOutcome;

/// <summary>
/// PayPal requires the shopper to complete a browser challenge (e.g. 3DS) before this payment
/// can be authorized. No hold was placed. The caller must not silently retry.
/// </summary>
public sealed record AuthorizePaymentRequiresAction(string PayPalOrderId, string PayerActionUrl) : AuthorizePaymentOutcome;

public record AuthorizationSnapshot(string AuthorizationId, string Status, decimal Amount, DateTimeOffset? ExpiresAt);

public record CaptureResult(string CaptureId, string Status, decimal CapturedAmount, decimal? FeeAmount, decimal? NetAmount);

public record RefundResult(string RefundId, string Status, decimal Amount);
