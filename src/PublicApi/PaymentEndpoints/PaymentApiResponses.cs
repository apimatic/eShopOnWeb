using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalRefundId { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Descriptor { get; set; } = string.Empty;
}

public class RefundStateModel
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>The payment state of a single order, returned by pay / fulfil / cancel.</summary>
public class PaymentStateResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? CardDescriptor { get; set; }
    public string? OperatorMessage { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<RefundStateModel> Refunds { get; set; } = new();

    public static PaymentStateResponse FromPayment(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        CaptureId = payment.CaptureId,
        CapturedGross = payment.CapturedGross,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        CardDescriptor = payment.CardDescriptor,
        OperatorMessage = payment.OperatorMessage,
        TotalRefunded = payment.TotalRefunded,
        Refunds = payment.Refunds
            .Select(r => new RefundStateModel
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status.ToString(),
                CreatedDate = r.CreatedDate
            })
            .ToList()
    };
}
