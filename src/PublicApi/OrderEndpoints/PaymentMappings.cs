using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class PaymentMappings
{
    public static PaymentStateDto ToDto(this Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpirationTime,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        SellerFee = payment.SellerFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            IdempotencyKey = r.IdempotencyKey,
            Amount = r.Amount,
            Currency = r.Currency,
            Status = r.Status,
            CreatedOn = r.CreatedOn
        }).ToList()
    };

    public static CardPaymentDetails ToCardDetails(this CardDetailsDto card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.Name,
        BillingAddress = card.BillingAddress is null
            ? null
            : new ApplicationCore.Entities.OrderAggregate.Address(
                card.BillingAddress.Street,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.Country,
                card.BillingAddress.ZipCode)
    };
}
