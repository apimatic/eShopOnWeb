using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), orders, http);
            })
            .Produces<OrderWithNotificationsView[]>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService orders, HttpContext http)
    {
        var result = await orders.ListMyOrdersAsync(http.User.RequireBuyerId(), http.RequestAborted);
        return Results.Ok(result);
    }
}
