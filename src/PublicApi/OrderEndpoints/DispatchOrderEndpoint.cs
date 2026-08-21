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

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderWorkflowService>
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
            (int orderId, IOrderWorkflowService orders) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orders);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderWorkflowService orders)
    {
        var order = await orders.DispatchAsync(request.OrderId);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(order.Id));
        var response = new DispatchOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}
