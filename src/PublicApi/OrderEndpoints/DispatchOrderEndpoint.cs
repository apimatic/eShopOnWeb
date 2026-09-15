using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: mark an order dispatched. The shopper is told it is on its way, and a "how did delivery
/// go?" follow-up is queued WITH THE PROVIDER for a few days later. Messaging failures never fail dispatch.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IReadRepository<Order>, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, orderRepository, notificationService))
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var ct = _httpContextAccessor.RequestAborted();

        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var outcome = await notificationService.NotifyOrderDispatchedAsync(order, ct);
        return outcome switch
        {
            OrderEventOutcome.AlreadyCancelled => Results.Conflict(new { orderId, message = "The order has been cancelled and cannot be dispatched." }),
            OrderEventOutcome.AlreadyDispatched => Results.Conflict(new { orderId, message = "The order has already been dispatched." }),
            _ => Results.Ok(new { orderId, status = "Dispatched" })
        };
    }
}
