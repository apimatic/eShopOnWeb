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

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOrderLifecycleService>
{
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderLifecycleService lifecycleService) =>
            {
                return await HandleAsync(orderId, lifecycleService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderLifecycleService lifecycleService)
    {
        var order = await lifecycleService.DispatchAsync(orderId);
        var notifications = await _notificationService.ListForOrderAsync(order.Id);
        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}
