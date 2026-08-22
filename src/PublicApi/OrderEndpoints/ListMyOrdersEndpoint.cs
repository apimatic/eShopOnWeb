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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), service, user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        var orders = await service.ListMyOrdersAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderDto.From).ToList()
        });
    }
}

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
