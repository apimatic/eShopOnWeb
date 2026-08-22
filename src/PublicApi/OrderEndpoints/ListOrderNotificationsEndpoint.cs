using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, service, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService service) =>
        HandleAsync(orderId, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var unauthorized = user.RequireBuyerId(out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var notifications = await service.GetOrderNotificationsAsync(orderId, buyerId, user.IsAdministrator());
        if (notifications is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ListOrderNotificationsResponse
        {
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        });
    }
}

public class ListOrderNotificationsResponse
{
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
