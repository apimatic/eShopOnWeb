using System.Linq;
using System.Threading;
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
/// Operator action: mark an order dispatched. The shopper is told it is on its way, and the delivery
/// follow-up is queued with the provider for a few days later. Restricted to administrators.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, service, http.RequestAborted);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var result = await service.DispatchOrderAsync(request.OrderId, ct);
        if (result.Status == OrderActionStatus.OrderNotFound)
        {
            return Results.NotFound();
        }

        var response = new OrderActionResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(result.Notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}
