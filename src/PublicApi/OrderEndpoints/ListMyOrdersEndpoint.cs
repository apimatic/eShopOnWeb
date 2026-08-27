using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = httpContext.User.GetBuyerId();
                var orders = await orderPaymentService.GetOrdersAsync(buyerId, cancellationToken);
                var payments = await orderPaymentService.GetPaymentsForOrdersAsync(
                    orders.Select(o => o.Id).ToList(), cancellationToken);

                var response = new ListMyOrdersResponse
                {
                    Orders = orders
                        .OrderByDescending(o => o.OrderDate)
                        .Select(o => OrderMapping.ToDto(o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
                        .ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
