using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepo.ListAsync(new OrdersByBuyerWithPaymentSpec(buyerId));

                var result = orders.Select(o => new MyOrderDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Status = o.Status.ToString(),
                    Total = o.Total(),
                    Items = o.OrderItems.Select(i => new MyOrderItemDto
                    {
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Quantity = i.Units
                    }).ToList(),
                    Payment = o.Payment == null ? null : new MyOrderPaymentDto
                    {
                        PayPalOrderId = o.Payment.PayPalOrderId,
                        AuthorizationId = o.Payment.AuthorizationId,
                        CaptureId = o.Payment.CaptureId,
                        CapturedAmount = o.Payment.CapturedAmount,
                        TotalRefunded = o.Payment.TotalRefunded,
                        Currency = o.Payment.Currency
                    }
                }).ToList();

                return Results.Ok(result);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }
}
