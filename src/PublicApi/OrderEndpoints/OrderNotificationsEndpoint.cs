using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the caller's orders, and what became of each message. Each entry carries its
/// own notificationId — the handle operator endpoints act on. Scoped to the caller's own orders.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest>
{
    private readonly IOrderNotificationService _service;

    public OrderNotificationsEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderNotificationsRequest { OrderId = orderId, CallerId = user.GetUserId() }, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderNotificationsRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, CancellationToken ct)
    {
        var response = new OrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();

        var notifications = await _service.GetOrderNotificationsAsync(request.OrderId, request.CallerId, ct);
        if (notifications is null) return Results.NotFound();

        response.Notifications = notifications.Select(NotificationDto.From).ToList();
        return Results.Ok(response);
    }
}
