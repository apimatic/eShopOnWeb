using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentEndpointHelpers
{
    public static string RequireBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            buyerId = user.FindFirstValue(ClaimTypes.Name);
        }

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException("The token does not contain a user name claim.", 401);
        }

        return buyerId!;
    }

    public static IResult Fail(PaymentOperationException exception) =>
        Results.Json(
            new ApiErrorResponse
            {
                Error = exception.Message,
                Code = exception.Code,
                LogRef = exception.LogRef
            },
            statusCode: exception.StatusCode);

    public static OrderDto ToDto(OrderSummary summary) =>
        new()
        {
            OrderId = summary.OrderId,
            OrderDate = summary.OrderDate,
            Total = summary.Total,
            PaymentStatus = summary.PaymentStatus,
            Payment = new PaymentDto
            {
                Status = summary.Payment.Status,
                PaymentMethodId = summary.Payment.PaymentMethodId,
                AuthorizationId = summary.Payment.AuthorizationId,
                AuthorizationState = summary.Payment.AuthorizationState,
                TransferId = summary.Payment.TransferId,
                AuthorizedAmountCents = summary.Payment.AuthorizedAmountCents,
                CapturedAmountCents = summary.Payment.CapturedAmountCents,
                FeeCents = summary.Payment.FeeCents,
                NetCents = summary.Payment.NetCents,
                RefundedAmountCents = summary.Payment.RefundedAmountCents,
                FailureCode = summary.Payment.FailureCode,
                FailureMessage = summary.Payment.FailureMessage
            },
            Items = summary.Items
                .Select(item => new OrderLineDto
                {
                    CatalogItemId = item.CatalogItemId,
                    ProductName = item.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                })
                .ToList()
        };
}
