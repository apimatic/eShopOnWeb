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
/// Operator action: cancel an order. The shopper is told, and any delivery follow-up that has not yet
/// gone out is called off at the provider so it can never reach them. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service, http.RequestAborted);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var result = await service.CancelOrderAsync(request.OrderId, ct);
        if (result.Status == OrderActionStatus.OrderNotFound)
        {
            return Results.NotFound();
        }

        var response = new OrderActionResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(result.Notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}
