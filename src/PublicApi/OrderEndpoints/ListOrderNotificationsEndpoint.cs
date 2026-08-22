using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService orders) =>
            {
                return await HandleAsync(orderId, user, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService orders)
        => HandleAsync(orderId, new ClaimsPrincipal(), orders);

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IOrderNotificationService orders)
    {
        try
        {
            var notifications = await orders.GetNotificationsAsync(
                orderId, HttpUser.GetBuyerId(user), HttpUser.IsAdministrator(user));
            return Results.Ok(new ListOrderNotificationsResponse
            {
                OrderId = orderId,
                Notifications = notifications.Select(OrderNotificationDtoMapper.From).ToList()
            });
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
