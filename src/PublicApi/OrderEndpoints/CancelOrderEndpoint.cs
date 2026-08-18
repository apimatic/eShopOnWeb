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
/// Operator action: cancel an order. The shopper is told, and any delivery follow-up still held by the
/// provider is called off so it never reaches them. Messaging failures never fail the cancellation.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IReadRepository<Order>, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
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

        var outcome = await notificationService.NotifyOrderCancelledAsync(order, ct);
        return outcome switch
        {
            OrderEventOutcome.AlreadyCancelled => Results.Conflict(new { orderId, message = "The order has already been cancelled." }),
            _ => Results.Ok(new { orderId, status = "Cancelled" })
        };
    }
}
