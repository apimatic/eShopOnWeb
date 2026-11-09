using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Save a card or a bank account. Card or bank details are passed straight to Stax and never stored or logged by the shop.
/// </summary>
public class SavePaymentMethodRequest
{
    /// <summary>"card" or "bank".</summary>
    public string? Type { get; set; }
    public string? Method { get; set; }
    public string? PersonName { get; set; }
    public string? CardNumber { get; set; }
    /// <summary>MMYY or MM/YY (or MM/YYYY).</summary>
    public string? CardExp { get; set; }
    public string? ExpMonth { get; set; }
    public string? ExpYear { get; set; }
    public string? Cvv { get; set; }
    public string? BankAccount { get; set; }
    public string? BankRouting { get; set; }
    /// <summary>checking or savings.</summary>
    public string? BankType { get; set; }
    /// <summary>personal or business.</summary>
    public string? BankHolderType { get; set; }
    public string? AddressZip { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Nickname { get; set; }
    public string? PersonName { get; set; }
    public string? CardExp { get; set; }
    public string? BankType { get; set; }
    public string? BankHolderType { get; set; }
    public string? CreatedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemovedAt { get; set; }
}

public class ShopperPaymentMethodsResponse
{
    public string Email { get; set; } = string.Empty;
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderItemRequest>? Items { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class PayRequest
{
    public string? PaymentMethodId { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    /// <summary>Sum of the items.</summary>
    public decimal Total { get; set; }
    /// <summary>Whether the order's payment has gone through (a bank payment only after the bank settled it).</summary>
    public bool Paid { get; set; }
    /// <summary>unpaid, processing, paid or failed.</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public decimal? Fee { get; set; }
    public decimal? AmountCharged { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentError { get; set; }
    /// <summary>none, pending, refunded or not_required.</summary>
    public string RefundStatus { get; set; } = "none";
}

public class AdminOrderDto : OrderDto
{
    public string? StaxTransactionId { get; set; }
    public string? RefundTransactionId { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundError { get; set; }
}

public class CheckoutResponse
{
    public int OrderId { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string PaymentMethodType { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
}

public class PayResponse : CheckoutResponse
{
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
}

public class RefundResponse
{
    public int OrderId { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Message { get; set; }
}

public class CreateInvoiceScheduleRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    /// <summary>yyyy-MM-dd. Today when omitted.</summary>
    public string? FirstInvoiceDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    /// <summary>paused or active.</summary>
    public string Status { get; set; } = string.Empty;
    public string? NextInvoiceAt { get; set; }
    public string? Rule { get; set; }
}

public class SellerContactRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class OnboardSellerRequest
{
    public string? Company { get; set; }
    public SellerContactRequest? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public class SellerDto
{
    public string SellerId { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Status { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
}
