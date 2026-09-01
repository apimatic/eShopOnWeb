using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;

// checkout_orders_v2 schemas.

internal sealed class PayPalCreateOrderRequest
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney Amount { get; set; } = new();
}

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("payment_source")] public PayPalPaymentSourceResponse? PaymentSource { get; set; }
    [JsonPropertyName("purchase_units")] public List<PayPalPurchaseUnitResponse>? PurchaseUnits { get; set; }
}

internal sealed class PayPalPurchaseUnitResponse
{
    [JsonPropertyName("reference_id")] public string? ReferenceId { get; set; }
    [JsonPropertyName("payments")] public PayPalPaymentCollection? Payments { get; set; }
}

internal sealed class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")] public List<PayPalAuthorization>? Authorizations { get; set; }
    [JsonPropertyName("captures")] public List<PayPalCapture>? Captures { get; set; }
    [JsonPropertyName("refunds")] public List<PayPalRefund>? Refunds { get; set; }
}

// payments_payment_v2 schemas.

internal sealed class PayPalAuthorization
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("expiration_time")] public DateTimeOffset? ExpirationTime { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; set; }
}

internal sealed class PayPalCaptureRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
}

internal sealed class PayPalReauthorizeRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
}

internal sealed class PayPalCapture
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("final_capture")] public bool? FinalCapture { get; set; }
    [JsonPropertyName("seller_receivable_breakdown")] public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    [JsonPropertyName("update_time")] public DateTimeOffset? UpdateTime { get; set; }
}

internal sealed class PayPalSellerReceivableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PaypalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
}

internal sealed class PayPalRefundRequest
{
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
}

internal sealed class PayPalRefund
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public PayPalMoney? Amount { get; set; }
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("note_to_payer")] public string? NoteToPayer { get; set; }
    [JsonPropertyName("seller_payable_breakdown")] public PayPalSellerPayableBreakdown? SellerPayableBreakdown { get; set; }
    [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
}

internal sealed class PayPalSellerPayableBreakdown
{
    [JsonPropertyName("gross_amount")] public PayPalMoney? GrossAmount { get; set; }
    [JsonPropertyName("paypal_fee")] public PayPalMoney? PaypalFee { get; set; }
    [JsonPropertyName("net_amount")] public PayPalMoney? NetAmount { get; set; }
    [JsonPropertyName("total_refunded_amount")] public PayPalMoney? TotalRefundedAmount { get; set; }
}
