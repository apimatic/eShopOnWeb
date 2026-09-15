using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class DispatchOrderRequest
{
    public int OrderId { get; set; }
    public CancellationToken Ct { get; set; }
}

/// <summary>
/// Operator action: mark an order dispatched. The shopper is told it is on its way, and a follow-up
/// asking how the delivery went is queued WITH THE PROVIDER for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId, Ct = ct }, service);
            })
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service)
    {
        var found = await service.DispatchAsync(request.OrderId, request.Ct);
        return found
            ? Results.Ok(new { orderId = request.OrderId, dispatched = true })
            : Results.NotFound();
    }
}
