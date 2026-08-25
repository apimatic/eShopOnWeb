using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The caller's own orders, each with its current payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, BuyerContext<IOrderPaymentService>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var context = new BuyerContext<IOrderPaymentService>(user.Identity!.Name!, orderPaymentService);
                return await HandleAsync(new MyOrdersRequest(), context);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, BuyerContext<IOrderPaymentService> context)
    {
        var response = new MyOrdersResponse(request.CorrelationId());
        var orders = await context.Service.GetOrdersForBuyerAsync(context.BuyerId, default);
        response.Orders = orders.Select(OrderDto.FromOrder).ToList();
        return Results.Ok(response);
    }
}
