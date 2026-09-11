using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- shared inputs -----

public record BillingAddressInput(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea2 = null,
    string? AdminArea1 = null,
    string? PostalCode = null)
{
    public BillingAddress ToDomain() => new(AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode, CountryCode);
}

/// <summary>Raw card details for a one-off payment or for saving. Never stored or logged.</summary>
public record CardInput(
    string Number,
    string Expiry,               // YYYY-MM
    string? SecurityCode = null,
    string? Name = null,
    BillingAddressInput? BillingAddress = null)
{
    public CardDetails ToDomain() => new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToDomain());
}

// ----- place order -----

public record OrderItemInput(int CatalogItemId, int Quantity);

public record ShipToAddressInput(string Street, string City, string State, string Country, string ZipCode);

public record PlaceOrderRequest(List<OrderItemInput> Items, ShipToAddressInput? ShipTo = null);

public record PlaceOrderResponse(int OrderId, string Status, decimal Total, string Currency);

// ----- pay -----

public record PayOrderRequest(CardInput? Card = null, int? SavedPaymentMethodId = null);

public record PayOrderResponse(int OrderId, string Status, string? AuthorizationId, decimal Amount, string Currency, DateTimeOffset? AuthorizationExpiresAt);

// ----- fulfil / cancel -----

public record PaymentStateResponse(
    int OrderId,
    string Status,
    string? AuthorizationId,
    string? CaptureId,
    decimal Amount,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

// ----- refunds -----

public record RefundOrderRequest(string IdempotencyKey, decimal? Amount = null);

public record RefundOrderResponse(string RefundId, string Status, decimal Amount, string Currency);

// ----- saved cards -----

public record SavePaymentMethodRequest(CardInput Card);

public record PaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);

// ----- my orders -----

public record MyOrderItem(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record MyOrderRefund(string? RefundId, decimal Amount, string Status);

public record MyOrderPayment(
    string Status,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal Amount,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency,
    IReadOnlyList<MyOrderRefund> Refunds);

public record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<MyOrderItem> Items,
    MyOrderPayment? Payment);

// ----- reconciliation -----

public record ReconciliationLineResponse(
    string? PayPalTransactionId,
    string? EventCode,
    string? Status,
    decimal? PayPalAmount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    int? OrderId,
    string MatchState);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLineResponse> Lines);
