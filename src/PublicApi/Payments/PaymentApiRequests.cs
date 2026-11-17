using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItem
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
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public long? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class PaymentMethodsResponse
{
    public List<Microsoft.eShopWeb.ApplicationCore.Payments.SavedCardResult> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class ReconciliationRequest : BaseRequest
{
    public System.DateTimeOffset From { get; set; }
    public System.DateTimeOffset To { get; set; }
}

public class MyOrdersRequest : BaseRequest
{
}

public class MyOrdersResponse
{
    public List<OrderSummary> Orders { get; set; } = new();
}
