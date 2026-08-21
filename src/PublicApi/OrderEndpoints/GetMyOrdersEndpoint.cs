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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ICheckoutService checkout) =>
            {
                return await HandleAsync(new GetMyOrdersRequest { BuyerId = EndpointUser.RequireBuyerId(http) }, checkout);
            })
            .Produces<GetMyOrdersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, ICheckoutService checkout)
    {
        var orders = await checkout.GetMyOrdersAsync(request.BuyerId!);
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o)).ToList()
        });
    }
}

public class GetMyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class GetMyOrdersResponse : BaseResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
