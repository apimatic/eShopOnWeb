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

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks the order dispatched. The shopper is told it is on its way and a delivery
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DispatchOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) => await HandleAsync(orderId, ct))
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var dispatched = await _orderNotificationService.DispatchAsync(orderId, ct);
        if (!dispatched)
            return Results.NotFound();

        return Results.Ok(new DispatchOrderResponse { OrderId = orderId, Status = "Dispatched" });
    }
}
