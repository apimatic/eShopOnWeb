using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromServices] IOrderNotificationService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service, ClaimsPrincipal user)
    {
        var userName = user.GetUserName();
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var ordersWithNotifications = await service.GetOrdersWithNotificationsForBuyerAsync(userName);
        var response = new MyOrdersResponse
        {
            Orders = ordersWithNotifications
                .Select(x => OrderDto.From(x.Order, x.Notifications.Select(n => NotificationDto.From(n, includeMessage: true))))
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
