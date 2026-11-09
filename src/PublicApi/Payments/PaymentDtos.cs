using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

// ---------- saved payment methods ----------

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>"card" or "bank"</summary>
    public string? Type { get; set; }

    /// <summary>Name on the card / account holder.</summary>
    public string? Name { get; set; }

    // card
    public string? CardNumber { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? Zip { get; set; }

    // bank
    public string? AccountNumber { get; set; }
    public string? RoutingNumber { get; set; }
    /// <summary>"checking" (default) or "savings"</summary>
    public string? AccountType { get; set; }
    /// <summary>"personal" (default) or "business"</summary>
    public string? HolderType { get; set; }

    /// <summary>Alternatively, a payment token created with Stax.js.</summary>
    public string? Token { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class SupportPaymentMethodDto : PaymentMethodDto
{
    public DateTimeOffset? RemovedAt { get; set; }
}

public class SupportPaymentMethodsResponse
{
    public string Email { get; set; } = string.Empty;
    public List<SupportPaymentMethodDto> PaymentMethods { get; set; } = new();
}

// ---------- orders ----------

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest>? Items { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShipToRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderPaymentMethodRequest : BaseRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    /// <summary>Card processing fee passed on to the shopper (0 until a payment is made).</summary>
    public decimal Fee { get; set; }
    /// <summary>What the shopper pays (or paid).</summary>
    public decimal Total { get; set; }
    /// <summary>Whether the payment has gone through.</summary>
    public bool Paid { get; set; }
    /// <summary>unpaid, processing, pending (bank payment awaiting settlement), paid or failed</summary>
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

public class CheckoutResponse
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string PaymentMethodType { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
}

public class AdminOrderDto : OrderDto
{
    public string Buyer { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    /// <summary>none, pending, refunded or failed</summary>
    public string RefundStatus { get; set; } = string.Empty;
    public decimal? RefundAmount { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
}

public class RefundResponse
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
}

// ---------- invoices ----------

public class CreateInvoiceScheduleRequest : BaseRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>yyyy-MM-dd; today when omitted.</summary>
    public string? FirstInvoiceDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    public string? FirstInvoiceDate { get; set; }
    /// <summary>active or paused</summary>
    public string Status { get; set; } = string.Empty;
    public string? NextInvoiceAt { get; set; }
}

// ---------- sellers ----------

public class OnboardSellerRequest : BaseRequest
{
    public string? Company { get; set; }
    public SellerContactRequest? Contact { get; set; }
}

public class SellerContactRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class SellerDto
{
    public string SellerId { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? Status { get; set; }
}
