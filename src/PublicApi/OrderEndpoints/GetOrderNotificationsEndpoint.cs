using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, HttpContext, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IOrderLifecycleService service) =>
            {
                var buyerId = http.RequireBuyerId();
                var notifications = await service.GetOrderNotificationsAsync(
                    orderId,
                    buyerId,
                    http.User.IsAdministrator());
                return Results.Ok(new GetOrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(NotificationDtoFactory.From).ToList()
                });
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext http, IOrderLifecycleService service)
        => Task.FromResult(Results.Ok());
}
