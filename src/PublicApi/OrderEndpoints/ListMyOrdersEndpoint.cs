using System.Collections.Generic;
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

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _http;

    public ListMyOrdersEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ICheckoutService checkout) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), checkout);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ICheckoutService checkout)
    {
        var buyerId = HttpUser.RequireBuyerId(_http.HttpContext!);
        var orders = await checkout.ListMyOrdersAsync(buyerId, _http.HttpContext!.RequestAborted);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderDto.From).ToList()
        });
    }
}
