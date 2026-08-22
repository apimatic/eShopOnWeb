using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOperatorOrderService>
{
    private readonly IRepository<OrderNotification> _notifications;

    public DispatchOrderEndpoint(IRepository<OrderNotification> notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), operatorOrderService);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOperatorOrderService operatorOrderService)
    {
        var response = new DispatchOrderResponse(request.CorrelationId());
        var order = await operatorOrderService.DispatchAsync(request.OrderId);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Notifications = NotificationMapping.ToDto(notifications).ToList();
        return Results.Ok(response);
    }
}
