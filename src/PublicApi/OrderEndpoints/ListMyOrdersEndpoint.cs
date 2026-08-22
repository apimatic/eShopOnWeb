using System.Collections.Generic;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderCheckoutService checkoutService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(user.Identity?.Name ?? string.Empty), checkoutService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderCheckoutService checkoutService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await checkoutService.ListMyOrdersAsync(request.BuyerId, default);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.Map).ToList()
        });
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; }

    public ListMyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
