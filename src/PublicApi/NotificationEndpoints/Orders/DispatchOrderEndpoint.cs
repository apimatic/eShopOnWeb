using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Orders;

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a follow-up
/// asking how the delivery went is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IPublicApiOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPublicApiOrderService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPublicApiOrderService service)
    {
        var result = await service.DispatchOrderAsync(orderId);
        if (result is null)
        {
            return Results.NotFound();
        }

        var response = new OrderActionResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Notifications = result.Notifications.Select(n => n.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
