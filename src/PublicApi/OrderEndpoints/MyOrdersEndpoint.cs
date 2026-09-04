using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<Order>, PayPalOptions>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, PayPalOptions payPalOptions) =>
            {
                return await HandleAsync(user, orderRepository, payPalOptions);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<Order> orderRepository, PayPalOptions payPalOptions)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        var orders = await orderRepository.ListAsync(new OrdersByBuyerWithPaymentSpec(buyerId));

        var response = new MyOrdersResponse()
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new OrderDto
                {
                    OrderId = o.Id,
                    Status = o.Status.ToString(),
                    OrderDate = o.OrderDate,
                    Total = o.Total(),
                    Currency = payPalOptions.Currency,
                    Items = o.OrderItems.Select(i => new OrderItemDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        Name = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Quantity = i.Units
                    }).ToList(),
                    Payment = ToPaymentDto(o.Payment)
                })
                .ToList()
        };

        return Results.Ok(response);
    }

    private static PaymentDto? ToPaymentDto(OrderPayment? payment)
    {
        if (payment is null) return null;

        var captured = payment.CapturedAmount ?? 0m;
        var refunded = payment.TotalRefundedAmount;

        string status;
        if (payment.CaptureId is not null)
        {
            status = refunded <= 0m
                ? "CAPTURED"
                : refunded >= captured ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        else if (string.Equals(payment.AuthorizationStatus, "VOIDED", System.StringComparison.OrdinalIgnoreCase))
        {
            status = "VOIDED";
        }
        else if (!string.IsNullOrEmpty(payment.AuthorizationId))
        {
            status = "AUTHORIZED";
        }
        else
        {
            status = "NONE";
        }

        return new PaymentDto
        {
            Status = status,
            AuthorizationId = payment.AuthorizationId,
            CaptureId = payment.CaptureId,
            Amount = payment.Amount,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = refunded,
            Currency = payment.Currency,
            PaymentSourceDescription = payment.PaymentSourceDescription
        };
    }
}