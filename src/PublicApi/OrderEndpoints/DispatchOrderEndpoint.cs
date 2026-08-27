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
/// Marks an order dispatched (operator). The shopper is told it is on its way and a
/// follow-up message is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderService, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderService orderService, INotificationService notificationService) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orderService, notificationService);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderService orderService, INotificationService notificationService)
    {
        var response = new DispatchOrderResponse(request.CorrelationId());

        var order = await orderService.DispatchOrderAsync(request.OrderId);

        await notificationService.NotifyOrderDispatchedAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();

        return Results.Ok(response);
    }
}
