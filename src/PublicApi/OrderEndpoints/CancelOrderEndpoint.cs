using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment. Releases any held funds; no money ever moves. Idempotent.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IOrderFulfilmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderFulfilmentService orderFulfilmentService) =>
            {
                return await HandleAsync(new OrderIdRequest(orderId), orderFulfilmentService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderFulfilmentService orderFulfilmentService)
    {
        var response = new CancelOrderResponse(request.CorrelationId());
        var order = await orderFulfilmentService.CancelAsync(request.OrderId);
        response.OrderId = order.Id;
        response.Order = OrderMapping.ToDto(order);
        return Results.Ok(response);
    }
}
