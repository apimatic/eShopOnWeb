using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: cancels the order. The shopper is told, and any follow-up the provider has not yet
/// sent is called off so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                var cancelled = await service.CancelAsync(orderId);
                return cancelled
                    ? Results.Ok(new { orderId, status = "cancelled" })
                    : Results.NotFound();
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService service) =>
        Task.FromResult(Results.Ok());
}
