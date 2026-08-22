using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PaymentMapping
{
    public static CardPaymentSource ToCardSource(CardDetailsRequest card)
    {
        var expiry = card.ResolveExpiry();
        var billing = card.BillingAddress;
        return new CardPaymentSource(
            NormalizeCardNumber(card.Number),
            expiry,
            string.IsNullOrWhiteSpace(card.SecurityCode) ? card.Cvv : card.SecurityCode,
            string.IsNullOrWhiteSpace(card.Name) ? "Jane Shopper" : card.Name.Trim(),
            new CardBillingAddress(
                string.IsNullOrWhiteSpace(billing?.AddressLine1) ? "2211 N First Street" : billing!.AddressLine1,
                billing?.AddressLine2,
                FirstNonEmpty(billing?.AdminArea2, billing?.City, "San Jose")!,
                FirstNonEmpty(billing?.AdminArea1, billing?.State, "CA"),
                string.IsNullOrWhiteSpace(billing?.PostalCode) ? "95131" : billing!.PostalCode,
                string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string NormalizeCardNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ApplicationCore.Exceptions.PaymentException(400, "Card number is required.");
        }

        return new string(number.Where(char.IsDigit).ToArray());
    }

    public static OrderResponse ToOrderResponse(Order order)
    {
        var response = new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = Money.ToCents(order.Total()),
            Currency = order.Payment.Currency,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = ToPaymentResponse(order)
        };
        return response;
    }

    public static PaymentResponse ToPaymentResponse(Order order)
    {
        return new PaymentResponse
        {
            PayPalOrderId = order.Payment.PayPalOrderId,
            AuthorizationId = order.Payment.AuthorizationId,
            AuthorizationStatus = order.Payment.AuthorizationStatus,
            AuthorizedAt = order.Payment.AuthorizedAt,
            AuthorizationExpiresAt = order.Payment.AuthorizationExpiresAt,
            CaptureId = order.Payment.CaptureId,
            CaptureStatus = order.Payment.CaptureStatus,
            CapturedAmount = order.Payment.CapturedAmount,
            PayPalFee = order.Payment.PayPalFee,
            NetAmount = order.Payment.NetAmount,
            RefundedAmount = order.Payment.RefundedAmount,
            RemainingRefundableAmount = order.Payment.RemainingRefundableAmount(),
            Currency = order.Payment.Currency,
            Refunds = order.Payment.Refunds.Select(r => new RefundResponse
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    public static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            LastFourDigits = method.LastFourDigits,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName,
            CreatedAt = method.CreatedAt
        };
}
