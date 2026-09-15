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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOperatorOrderService>
{
    private readonly IRepository<OrderNotification> _notifications;

    public CancelOrderEndpoint(IRepository<OrderNotification> notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), operatorOrderService);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOperatorOrderService operatorOrderService)
    {
        var response = new CancelOrderResponse(request.CorrelationId());
        var order = await operatorOrderService.CancelAsync(request.OrderId);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Notifications = NotificationMapping.ToDto(notifications).ToList();
        return Results.Ok(response);
    }
}
