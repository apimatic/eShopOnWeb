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
/// Operator action: fulfils an authorized order, which is when the held funds are actually captured.
/// Renews a stale authorization first if needed. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IOrderFulfilmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderFulfilmentService orderFulfilmentService) =>
            {
                return await HandleAsync(new OrderIdRequest(orderId), orderFulfilmentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderFulfilmentService orderFulfilmentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());
        var order = await orderFulfilmentService.FulfilAsync(request.OrderId);
        response.OrderId = order.Id;
        response.Order = OrderMapping.ToDto(order);
        return Results.Ok(response);
    }
}
