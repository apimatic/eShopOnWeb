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
/// Operator action: cancels an order. The shopper is told, and any queued follow-up that has not yet
/// gone out is called off with the provider so a cancelled order never triggers a delivery-feedback text.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ISmsNotificationService service) =>
                await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service))
            .Produces<OrderTransitionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ISmsNotificationService service)
    {
        var exists = await service.CancelOrderAsync(request.OrderId);
        return exists
            ? Results.Ok(new OrderTransitionResponse(request.CorrelationId()) { OrderId = request.OrderId, Status = "Cancelled" })
            : Results.NotFound();
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}
