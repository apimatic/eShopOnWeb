using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderWorkflowService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IOrderWorkflowService service) =>
            {
                return await HandleAsync(orderId, httpContext, service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IOrderWorkflowService orderWorkflowService)
        => HandleAsync(request, null!, orderWorkflowService);

    private async Task<IResult> HandleAsync(int orderId, HttpContext httpContext, IOrderWorkflowService service)
    {
        var buyerId = httpContext.User.Identity?.Name ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (buyerId == null)
        {
            return Results.Unauthorized();
        }

        var order = await service.GetBuyerOrderAsync(buyerId, orderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForBuyerOrderAsync(buyerId, orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList()
        });
    }
}
