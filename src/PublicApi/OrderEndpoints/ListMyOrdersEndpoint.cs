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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = ApiUser.GetBuyerId(user) }, checkout);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ICheckoutService checkout)
    {
        var orders = await checkout.ListMyOrdersAsync(request.BuyerId!);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(pair => PaymentResponseMapper.Map(pair.Order, pair.Payment)).ToList()
        });
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListMyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
