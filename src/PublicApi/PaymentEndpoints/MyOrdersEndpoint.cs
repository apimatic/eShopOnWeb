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

/// <summary>Lists the signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user, IReadRepository<Order> orderRepository, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var orders = await orderRepository.ListAsync(
                    new CustomerOrdersWithPaymentSpecification(buyerId), ct);
                var response = orders.Select(OrderResponse.FromOrder).ToList();
                return Results.Ok(response);
            })
            .Produces<System.Collections.Generic.List<OrderResponse>>()
            .WithTags("OrderEndpoints");
    }
}
