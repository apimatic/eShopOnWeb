using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateShopOrderRequest : BaseRequest
{
    public List<CreateShopOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class CreateShopOrderItemRequest
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

public class ShopOrderResponse : BaseResponse
{
    public ShopOrderResponse(Guid correlationId) : base(correlationId) { }
    public ShopOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<ShopOrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();

    public static ShopOrderResponse From(ShopOrderResult result, Guid correlationId)
    {
        return new ShopOrderResponse(correlationId)
        {
            OrderId = result.OrderId,
            Status = result.Status,
            Total = result.Total,
            Currency = result.Currency,
            OrderDate = result.OrderDate,
            PayPalOrderId = result.PayPalOrderId,
            AuthorizationId = result.AuthorizationId,
            AuthorizationStatus = result.AuthorizationStatus,
            AuthorizationExpiration = result.AuthorizationExpiration,
            CaptureId = result.CaptureId,
            CaptureStatus = result.CaptureStatus,
            CapturedAmount = result.CapturedAmount,
            PaypalFee = result.PaypalFee,
            NetAmount = result.NetAmount,
            RemainingRefundable = result.RemainingRefundable,
            Items = result.Items.Select(i => new ShopOrderItemResponse
            {
                CatalogItemId = i.CatalogItemId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList(),
            Refunds = result.Refunds.Select(r => RefundResponse.From(r)).ToList()
        };
    }
}

public class ShopOrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class RefundResponse : BaseResponse
{
    public RefundResponse(Guid correlationId) : base(correlationId) { }
    public RefundResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public static RefundResponse From(ShopRefundResult result, Guid? correlationId = null)
    {
        var response = correlationId is Guid id ? new RefundResponse(id) : new RefundResponse();
        response.RefundId = result.RefundId;
        response.PayPalRefundId = result.PayPalRefundId;
        response.Status = result.Status;
        response.Amount = result.Amount;
        response.Currency = result.Currency;
        return response;
    }
}
