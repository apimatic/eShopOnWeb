using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderService orderService) =>
            {
                return await HandleAsync(orderId, orderService);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderService orderService)
    {
        var result = await orderService.DispatchAsync(orderId);
        if (result == null)
        {
            return Results.NotFound();
        }

        if (result.StatusChanged)
        {
            await _notificationService.TryNotifyOrderDispatchedAsync(result.Order);
        }

        return Results.Ok(new DispatchOrderResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString()
        });
    }
}
