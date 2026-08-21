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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(BuyerIdentity.Require(user)), checkout);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IOrderCheckoutService checkout)
    {
        var orders = await checkout.ListMyOrdersAsync(request.BuyerId);
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(OrderResponse.From).ToList()
        });
    }
}

public class GetMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; }

    public GetMyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}

public class GetMyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
