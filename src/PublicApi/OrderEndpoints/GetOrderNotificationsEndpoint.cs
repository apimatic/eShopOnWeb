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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService notificationService, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, notificationService, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int orderId, IOrderNotificationService notificationService, HttpContext httpContext)
    {
        var notifications = await notificationService.GetOrderNotificationsAsync(
            orderId,
            httpContext.GetRequiredBuyerId(),
            httpContext.IsAdministrator());

        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}
