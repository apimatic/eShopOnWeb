using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using CardDetails = Microsoft.eShopWeb.ApplicationCore.PayPal.CardDetails;
using BillingAddressDetails = Microsoft.eShopWeb.ApplicationCore.PayPal.BillingAddressDetails;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

/// <summary>Maps between API DTOs, domain entities, and gateway inputs, and translates results to HTTP.</summary>
public static class PaymentApiMapping
{
    /// <summary>The caller's identity from the JWT — used as the buyer id for their own data.</summary>
    public static string? GetBuyerId(this HttpContext httpContext)
    {
        var user = httpContext.User;
        return user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
    }

    public static Address ToAddress(this AddressDto? dto) =>
        dto is null
            ? new Address("Not provided", "Not provided", "N/A", "Not provided", "00000")
            : new Address(
                string.IsNullOrWhiteSpace(dto.Street) ? "Not provided" : dto.Street,
                string.IsNullOrWhiteSpace(dto.City) ? "Not provided" : dto.City,
                dto.State ?? "N/A",
                string.IsNullOrWhiteSpace(dto.Country) ? "Not provided" : dto.Country,
                string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);

    public static CardDetails ToCardDetails(this CardDto card)
    {
        var year = card.ExpiryYear < 100 ? 2000 + card.ExpiryYear : card.ExpiryYear;
        var expiry = $"{year:D4}-{card.ExpiryMonth:D2}";

        BillingAddressDetails? billing = card.BillingAddress is null
            ? null
            : new BillingAddressDetails(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);

        return new CardDetails(card.Number ?? string.Empty, expiry, card.SecurityCode, card.Name, billing);
    }

    public static OrderResponse ToResponse(this Order order)
    {
        var response = new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.CurrencyCode ?? string.Empty,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (order.Payment is not null)
        {
            response.Payment = order.Payment.ToResponse();
        }

        return response;
    }

    public static PaymentResponse ToResponse(this Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PaymentSource = payment.PaymentSourceDescription,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundResponseItem
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
    };

    public static PaymentMethodResponse ToResponse(this SavedPaymentMethod method) => new()
    {
        Id = method.Id,
        CardBrand = method.CardBrand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };

    public static ReconciliationResponse ToResponse(this ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        Currency = report.CurrencyCode,
        PayPalTransactionCount = report.PayPalTransactionCount,
        EshopOrderCount = report.EshopOrderCount,
        MatchedCount = report.Matched.Count,
        Matched = report.Matched.Select(ToLine).ToList(),
        OnlyInPayPal = report.OnlyInPayPal.Select(ToLine).ToList(),
        OnlyInEshop = report.OnlyInEshop.Select(ToLine).ToList()
    };

    private static ReconciliationLineResponse ToLine(ReconciliationLine line) => new()
    {
        MatchState = line.MatchState,
        EshopOrderId = line.EshopOrderId,
        PayPalTransactionId = line.PayPalTransactionId,
        PayPalOrderId = line.PayPalOrderId,
        CaptureId = line.CaptureId,
        InvoiceId = line.InvoiceId,
        EshopAmount = line.EshopAmount,
        PayPalAmount = line.PayPalAmount,
        PayPalStatus = line.PayPalStatus,
        Date = line.Date
    };

    // ---- Result -> HTTP problem translation ----

    /// <summary>Builds an error HTTP result from a failed Ardalis result.</summary>
    public static Microsoft.AspNetCore.Http.IResult ToProblem(this Ardalis.Result.IResult result)
    {
        var messages = result.Errors
            .Concat(result.ValidationErrors.Select(v => v.ErrorMessage))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray();
        var detail = messages.Length > 0 ? string.Join(" ", messages) : null;

        return result.Status switch
        {
            ResultStatus.NotFound => Results.Problem(detail ?? "Not found.", statusCode: StatusCodes.Status404NotFound),
            ResultStatus.Invalid => Results.Problem(detail ?? "Invalid request.", statusCode: StatusCodes.Status400BadRequest),
            ResultStatus.Unauthorized => Results.Problem(detail ?? "Unauthorized.", statusCode: StatusCodes.Status401Unauthorized),
            ResultStatus.Forbidden => Results.Problem(detail ?? "Forbidden.", statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Error => Results.Problem(detail ?? "The payment could not be processed.", statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Problem(detail ?? "Unexpected error.", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
