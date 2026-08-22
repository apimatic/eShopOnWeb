using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderService orderService) =>
            {
                return await HandleAsync(orderId, orderService);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderService orderService)
    {
        var result = await orderService.CancelAsync(orderId);
        if (result == null)
        {
            return Results.NotFound();
        }

        await _notificationService.CancelPendingFollowUpsAsync(result.Order.Id);

        if (result.StatusChanged)
        {
            await _notificationService.TryNotifyOrderCancelledAsync(result.Order);
        }

        return Results.Ok(new CancelOrderResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString()
        });
    }
}
