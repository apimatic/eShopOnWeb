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

public class GetMyOrdersEndpoint : IEndpoint<IResult, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutPaymentService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ICheckoutPaymentService service) =>
        HandleAsync(service, null!);

    private async Task<IResult> HandleAsync(ICheckoutPaymentService service, HttpContext http)
    {
        var buyerId = EndpointIdentity.RequireUserName(http);
        var orders = await service.ListMyOrdersAsync(buyerId, http.RequestAborted);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.From).ToList()
        });
    }
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}
