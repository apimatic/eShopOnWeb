using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, BaseRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListMyOrdersRequest { BuyerId = buyerId }, service);
            })
            .Produces<OrderListResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(BaseRequest request, IOrderPaymentService service)
    {
        var typed = (ListMyOrdersRequest)request;
        var orders = await service.ListShopperOrdersAsync(typed.BuyerId);
        var response = new OrderListResponse(typed.CorrelationId());
        response.Orders.AddRange(orders.Select(o => OrderResponseMapper.ToResponse(o, typed.CorrelationId())));
        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}
