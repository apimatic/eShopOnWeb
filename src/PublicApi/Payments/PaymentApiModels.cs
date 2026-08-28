using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PlaceOrderRequest
{
    public IReadOnlyList<PlaceOrderItemRequest> Items { get; set; } = Array.Empty<PlaceOrderItemRequest>();
    public ShippingAddressRequest ShipToAddress { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class CardRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public CardBillingAddressRequest BillingAddress { get; set; } = new();

    public CardInput ToInput() => new(Name, Number, Expiry, SecurityCode,
        new CardBillingAddressInput(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.AdminArea2, BillingAddress.AdminArea1, BillingAddress.PostalCode,
            BillingAddress.CountryCode));
}

public sealed class CardBillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string? AdminArea1 { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequestDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    public CardRequestDto Card { get; set; } = new();
}

public sealed class CreateRefundRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundView(string? RefundId, string IdempotencyKey, decimal Amount, string Currency, string Status, string? StatusReason);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, decimal Total, string? Currency,
    string PaymentStatus, string FulfilmentStatus, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, string? CaptureId, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetProceeds, string? PaymentFailureReason,
    IReadOnlyList<OrderItemView> Items, IReadOnlyList<RefundView> Refunds);
public sealed record PaymentMethodView(int PaymentMethodId, string? Name, string? Brand, string? LastDigits, string? Expiry, string? Type);
public sealed record ReconciliationTransactionView(string? TransactionId, string? PayPalReferenceId, string? EventCode,
    DateTimeOffset? InitiationDate, decimal? Amount, decimal? Fee, string? Currency, string? Status,
    string? InvoiceId, int? OrderId);
public sealed record ReconciliationOrderView(int OrderId, string? PayPalOrderId, string? AuthorizationId,
    string? CaptureId, IReadOnlyList<string> RefundIds, decimal Total, string PaymentStatus);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To, bool ProviderDataAvailable,
    bool ReportingLagMayApply, IReadOnlyList<ReconciliationTransactionView> Transactions,
    IReadOnlyList<ReconciliationOrderView> LocalOnly);
