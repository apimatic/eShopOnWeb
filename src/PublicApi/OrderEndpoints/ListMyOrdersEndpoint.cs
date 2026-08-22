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

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse
{
    public ListMyOrdersResponse() { }

    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), checkout);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var orders = await checkout.ListMyOrdersAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.Map).ToList()
        });
    }
}
