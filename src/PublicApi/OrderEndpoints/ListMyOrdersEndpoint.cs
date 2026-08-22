using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderPaymentService orders, HttpContext http) =>
            {
                return await HandleAsync(CreateOrderEndpoint.RequireUserName(http.User), orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderPaymentService orders)
    {
        var list = await orders.ListMyOrdersAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = list.Select(OrderDto.From).ToList()
        });
    }
}

public class ListMyOrdersResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
