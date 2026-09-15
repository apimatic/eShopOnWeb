using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Mapping helpers shared by the payment / order / saved-card endpoints.</summary>
internal static class PaymentMapping
{
    /// <summary>The caller's identity name, which is the order/card owner (BuyerId).</summary>
    public static string? GetBuyerId(ClaimsPrincipal user) => user?.Identity?.Name;

    public static CardDetails ToCardDetails(CardDto card) => new CardDetails(
        Number: card.Number,
        Expiry: card.Expiry,
        SecurityCode: card.SecurityCode,
        Name: card.Name,
        BillingAddress: card.BillingAddress is null
            ? null
            : new CardBillingAddress(
                Line1: card.BillingAddress.Line1,
                Line2: card.BillingAddress.Line2,
                City: card.BillingAddress.City,
                State: card.BillingAddress.State,
                PostalCode: card.BillingAddress.PostalCode,
                CountryCode: card.BillingAddress.CountryCode));

    public static PaymentStateDto? ToPaymentState(Order order)
    {
        var payment = order.Payment;
        if (payment is null)
        {
            return null;
        }

        return new PaymentStateDto
        {
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded(),
            RemainingRefundable = payment.RemainingRefundable(),
            Refunds = payment.Refunds
                .Select(r => new RefundDto
                {
                    RefundId = r.RefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                })
                .ToList()
        };
    }

    public static OrderPaymentResponse ToOrderPaymentResponse(Order order) => new OrderPaymentResponse
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        PaymentStatus = order.PaymentStatus.ToString(),
        Payment = ToPaymentState(order)
    };
}
