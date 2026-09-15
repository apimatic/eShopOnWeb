using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new(
        Street ?? "123 Eshop Street",
        City ?? "San Jose",
        State ?? "CA",
        Country ?? "United States",
        ZipCode ?? "95131");
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class CreateOrderApiRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
    internal string? BuyerId { get; set; }
}

public class PayOrderApiRequest : BaseRequest
{
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    internal int OrderId { get; set; }
    internal string? BuyerId { get; set; }
}

public class RefundOrderApiRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    internal int OrderId { get; set; }
    internal string? BuyerId { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PaymentStateDto
{
    public string Status { get; set; } = string.Empty;
    public string? PayPalCheckoutOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalAuthorizationStatus { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalCaptureStatus { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? Currency { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto Payment { get; set; } = new();
}

public class CreateOrderApiResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class OrderApiResponse : BaseResponse
{
    public OrderDto Order { get; set; } = new();
}

public class RefundApiResponse : BaseResponse
{
    public int RefundId { get; set; }
    public OrderRefundDto Refund { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class ListOrdersApiResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
