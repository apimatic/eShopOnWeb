using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("unique_name");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApplicationCore.Exceptions.PaymentException("The caller is not authenticated.", 401);
        }

        return name;
    }

    public static object ToResponse(this Order order)
    {
        return new
        {
            orderId = order.Id,
            buyerId = order.BuyerId,
            status = order.PaymentStatus.ToString(),
            orderDate = order.OrderDate,
            total = order.Total(),
            currency = order.Currency,
            remainingRefundable = order.RemainingRefundable(),
            payment = new
            {
                payPalOrderId = order.PayPalOrderId,
                authorizationId = order.PayPalAuthorizationId,
                authorizationStatus = order.PayPalAuthorizationStatus,
                authorizedAt = order.AuthorizedAt,
                authorizationExpirationTime = order.AuthorizationExpirationTime,
                captureId = order.PayPalCaptureId,
                captureStatus = order.PayPalCaptureStatus,
                capturedAmount = order.CapturedAmount,
                paypalFee = order.PaypalFee,
                netProceeds = order.NetProceeds,
                capturedAt = order.CapturedAt,
                refunds = order.Refunds.Select(r => new
                {
                    refundId = r.Id,
                    payPalRefundId = r.PayPalRefundId,
                    status = r.Status,
                    amount = r.Amount,
                    currency = r.Currency,
                    idempotencyKey = r.IdempotencyKey,
                    createdAt = r.CreatedAt
                })
            },
            items = order.OrderItems.Select(i => new
            {
                catalogItemId = i.ItemOrdered.CatalogItemId,
                productName = i.ItemOrdered.ProductName,
                unitPrice = i.UnitPrice,
                quantity = i.Units
            }),
            shipToAddress = new
            {
                street = order.ShipToAddress.Street,
                city = order.ShipToAddress.City,
                state = order.ShipToAddress.State,
                country = order.ShipToAddress.Country,
                zipCode = order.ShipToAddress.ZipCode
            }
        };
    }
}
