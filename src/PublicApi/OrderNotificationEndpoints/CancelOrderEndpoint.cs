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

public class CancelOrderRequest
{
    public int OrderId { get; set; }
    public CancellationToken Ct { get; set; }
}

/// <summary>
/// Operator action: cancel an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off with the provider so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId, Ct = ct }, service);
            })
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service)
    {
        var found = await service.CancelAsync(request.OrderId, request.Ct);
        return found
            ? Results.Ok(new { orderId = request.OrderId, cancelled = true })
            : Results.NotFound();
    }
}
