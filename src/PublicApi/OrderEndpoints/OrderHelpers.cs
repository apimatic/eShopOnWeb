using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderHelpers
{
    internal static string GetBuyerId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name
            ?? throw new System.UnauthorizedAccessException("Cannot determine caller identity.");
    }

    internal static OrderPaymentDto ToDto(OrderPayment? payment) =>
        payment == null ? new OrderPaymentDto(null, "Pending", null, null, null, null, null, null) :
        new OrderPaymentDto(
            payment.PayPalOrderId,
            payment.PaymentStatus,
            payment.AuthorizationId,
            payment.CaptureId,
            payment.CapturedAmount,
            payment.PayPalFeeAmount,
            payment.NetAmount,
            payment.TotalRefundedAmount);
}

public record OrderPaymentDto(
    string? PayPalOrderId,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFeeAmount,
    decimal? NetAmount,
    decimal? TotalRefundedAmount);
