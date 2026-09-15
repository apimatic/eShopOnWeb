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

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext http) =>
            {
                return await HandleAsync(service, http);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService service)
        => HandleAsync(service, http: null!);

    private async Task<IResult> HandleAsync(IOrderPaymentService service, HttpContext http)
    {
        var orders = await service.ListMyOrdersAsync(http.RequireBuyerId(), http.RequestAborted);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.Map).ToList()
        });
    }
}
