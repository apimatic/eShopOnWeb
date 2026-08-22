using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse : BaseResponse
{
    public OrderResponse(Guid correlationId) : base(correlationId) { }
    public OrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();

    public static OrderResponse From(OrderPaymentResult result, Guid? correlationId = null)
    {
        var response = correlationId.HasValue
            ? new OrderResponse(correlationId.Value)
            : new OrderResponse();
        response.OrderId = result.OrderId;
        response.Status = result.Status;
        response.Total = result.Total;
        response.Currency = result.Currency;
        response.OrderDate = result.OrderDate;
        response.PayPalOrderId = result.PayPalOrderId;
        response.AuthorizationId = result.AuthorizationId;
        response.AuthorizationStatus = result.AuthorizationStatus;
        response.AuthorizationExpiresAt = result.AuthorizationExpiresAt;
        response.CaptureId = result.CaptureId;
        response.CaptureStatus = result.CaptureStatus;
        response.CapturedAmount = result.CapturedAmount;
        response.PaypalFee = result.PaypalFee;
        response.NetAmount = result.NetAmount;
        response.RefundableAmount = result.RefundableAmount;
        foreach (var item in result.Items)
        {
            response.Items.Add(new OrderItemResponse
            {
                CatalogItemId = item.CatalogItemId,
                ProductName = item.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        foreach (var refund in result.Refunds)
        {
            response.Refunds.Add(RefundResponse.From(refund));
        }

        return response;
    }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponse : BaseResponse
{
    public RefundResponse(Guid correlationId) : base(correlationId) { }
    public RefundResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public static RefundResponse From(RefundResult result, Guid? correlationId = null)
    {
        var response = correlationId.HasValue
            ? new RefundResponse(correlationId.Value)
            : new RefundResponse();
        response.RefundId = result.RefundId;
        response.PayPalRefundId = result.PayPalRefundId;
        response.Amount = result.Amount;
        response.Status = result.Status;
        response.IdempotencyKey = result.IdempotencyKey;
        return response;
    }
}
