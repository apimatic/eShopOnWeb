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

/// <summary>
/// The caller's own orders, each showing where its notifications got to (delivery outcomes are
/// refreshed from the provider).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersCommand, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var ownerId = user.UserName();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
                return await HandleAsync(new MyOrdersCommand(ownerId), service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersCommand request, IOrderNotificationService service)
    {
        var orders = await service.GetOrdersForOwnerAsync(request.OwnerId);
        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await service.GetNotificationsForOrdersAsync(orderIds);
        var byOrder = notifications.ToLookup(n => n.OrderId);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => MyOrderDto.From(o, byOrder[o.Id])).ToList()
        };
        return Results.Ok(response);
    }
}
