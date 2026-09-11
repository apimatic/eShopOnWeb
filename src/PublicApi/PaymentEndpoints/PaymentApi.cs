using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment/saved-card endpoints: identity, Result mapping, DTO shaping.</summary>
public static class PaymentApi
{
    /// <summary>The caller's shopper identity, taken from the bearer token (never from the request body).</summary>
    public static string? BuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static IResult ToHttp<T>(Result<T> result, Func<T, object>? shape = null) =>
        result.Status switch
        {
            ResultStatus.Ok => Results.Ok(shape is null ? (object?)result.Value : shape(result.Value)),
            ResultStatus.NotFound => Results.NotFound(Problem(result.Errors, "Not found.")),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToArray() }),
            ResultStatus.Forbidden => Results.Json(Problem(result.Errors, "Forbidden."), statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Json(Problem(result.Errors, "Unauthorized."), statusCode: StatusCodes.Status401Unauthorized),
            _ => Results.Json(Problem(result.Errors, "The payment provider could not complete the request."), statusCode: StatusCodes.Status502BadGateway),
        };

    public static IResult ToHttp(Result result, Func<IResult> onSuccess) =>
        result.Status switch
        {
            ResultStatus.Ok => onSuccess(),
            ResultStatus.NotFound => Results.NotFound(Problem(result.Errors, "Not found.")),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToArray() }),
            ResultStatus.Forbidden => Results.Json(Problem(result.Errors, "Forbidden."), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(Problem(result.Errors, "The payment provider could not complete the request."), statusCode: StatusCodes.Status502BadGateway),
        };

    private static object Problem(IEnumerable<string> errors, string fallback)
    {
        var list = errors?.ToArray() ?? Array.Empty<string>();
        return new { errors = list.Length > 0 ? list : new[] { fallback } };
    }

    // ---- DTO shaping ---------------------------------------------------------------------------
    public static object PaymentDto(Payment p) => new
    {
        orderId = p.OrderId,
        paymentStatus = p.Status.ToString(),
        amount = p.Amount,
        currency = p.CurrencyCode,
        paypalOrderId = p.PayPalOrderId,
        authorization = new { id = p.AuthorizationId, status = p.AuthorizationStatus, expiresAt = p.AuthorizationExpiresAt },
        capture = p.CaptureId is null ? null : new
        {
            id = p.CaptureId,
            status = p.CaptureStatus,
            capturedAmount = p.CapturedAmount,
            paypalFee = p.PayPalFee,
            netAmount = p.NetAmount
        },
        card = p.CardBrand is null && p.CardLast4 is null ? null : new { brand = p.CardBrand, last4 = p.CardLast4 },
        refunds = p.Refunds.Select(r => new { r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt }).ToArray(),
        totalRefunded = p.TotalRefunded(),
        refundableRemaining = p.RefundableRemaining()
    };

    public static object OrderWithPaymentDto(OrderWithPayment ow)
    {
        var o = ow.Order;
        return new
        {
            orderId = o.Id,
            orderDate = o.OrderDate,
            orderStatus = o.Status.ToString(),
            total = o.Total(),
            items = o.OrderItems.Select(i => new
            {
                catalogItemId = i.ItemOrdered.CatalogItemId,
                productName = i.ItemOrdered.ProductName,
                unitPrice = i.UnitPrice,
                units = i.Units
            }).ToArray(),
            payment = ow.Payment is null ? null : PaymentDto(ow.Payment)
        };
    }

    public static object SavedCardDto(SavedPaymentMethod m) => new
    {
        paymentMethodId = m.Id,
        brand = m.CardBrand,
        last4 = m.CardLast4,
        expiry = m.ExpiryMonthYear,
        cardholderName = m.CardholderName,
        createdAt = m.CreatedAt
    };
}
