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

/// <summary>The caller's own orders, each showing where its notifications got to (provider status refreshed).</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public MyOrdersEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var username = user.GetUserName();
        if (username is null)
        {
            return Results.Unauthorized();
        }

        var ordersWithNotifications = await _orderNotificationService.GetMyOrdersAsync(username);
        var response = new MyOrdersResponse
        {
            Orders = ordersWithNotifications
                .Select(o => OrderSummaryDto.From(o.Order, o.Notifications))
                .OrderByDescending(o => o.OrderDate)
                .ToList()
        };
        return Results.Ok(response);
    }
}
