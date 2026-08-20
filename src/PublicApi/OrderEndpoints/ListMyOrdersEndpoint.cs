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
    public string? BuyerId { get; set; }
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ICheckoutService checkout) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = Caller.UserName(http) }, checkout);
            })
            .Produces<ListOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ICheckoutService checkout)
    {
        var orders = await checkout.ListMyOrdersAsync(request.BuyerId!);
        var response = new ListOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o, checkout.Currency)).ToList()
        };
        return Results.Ok(response);
    }
}
