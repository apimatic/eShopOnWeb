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

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse()
    {
        Orders = new System.Collections.Generic.List<OrderDto>();
    }

    public System.Collections.Generic.List<OrderDto> Orders { get; set; }
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext) }, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.ListMyOrdersAsync(request.BuyerId!, default);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderDtoMapper.Map).ToList()
        });
    }
}
