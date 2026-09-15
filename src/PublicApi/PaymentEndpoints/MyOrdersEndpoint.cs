using System.Linq;
using System.Security.Claims;
using System.Threading;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders with their payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(order => new OrderSummaryDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        PaymentStatus = order.PaymentStatus.ToString(),
                        Payment = PaymentMapping.ToPaymentState(order)
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
