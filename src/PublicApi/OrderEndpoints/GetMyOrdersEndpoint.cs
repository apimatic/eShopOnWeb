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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IPaidOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaidOrderService service, ClaimsPrincipal user) => await HandleAsync(service, user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IPaidOrderService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(IPaidOrderService service, ClaimsPrincipal user)
    {
        var orders = await service.GetMyOrdersAsync(user.GetRequiredUserName());
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(OrderDtoMapper.ToDto).ToList()
        });
    }
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
