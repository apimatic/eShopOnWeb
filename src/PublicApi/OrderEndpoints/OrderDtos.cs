using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matches { get; set; } = new();
    public List<PaypalOnlyTransactionDto> PaypalOnly { get; set; } = new();
    public List<EshopOnlyPaymentDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string? PaypalTransactionId { get; set; }
    public string MatchReason { get; set; } = string.Empty;
}

public class PaypalOnlyTransactionDto
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? InitiationDate { get; set; }
}

public class EshopOnlyPaymentDto
{
    public int OrderId { get; set; }
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateDto
{
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = currency,
            OrderDate = order.OrderDate,
            Payment = new PaymentStateDto
            {
                PaypalOrderId = order.PaypalOrderId,
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                AuthorizationExpirationTime = order.AuthorizationExpirationTime,
                CaptureId = order.CaptureId,
                CaptureStatus = order.CaptureStatus,
                CapturedAmount = order.CapturedGross,
                PaypalFee = order.PaypalFee,
                NetAmount = order.NetAmount,
                RemainingRefundable = order.RemainingRefundable(),
                Refunds = order.Refunds.Select(r => new RefundDto
                {
                    RefundId = r.PaypalRefundId,
                    Amount = r.Amount,
                    Status = r.Status
                }).ToList()
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
    }

    public static ReconciliationResponse ToResponse(ReconciliationReport report, Guid correlationId)
    {
        return new ReconciliationResponse(correlationId)
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                PaypalTransactionId = m.PaypalTransactionId,
                MatchReason = m.MatchReason
            }).ToList(),
            PaypalOnly = report.PaypalOnly.Select(t => new PaypalOnlyTransactionDto
            {
                TransactionId = t.TransactionId,
                PaypalReferenceId = t.PaypalReferenceId,
                Status = t.TransactionStatus,
                Amount = t.Amount,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopOnlyPaymentDto
            {
                OrderId = e.OrderId,
                PaypalOrderId = e.PaypalOrderId,
                AuthorizationId = e.AuthorizationId,
                CaptureId = e.CaptureId,
                Status = e.Status
            }).ToList()
        };
    }
}
