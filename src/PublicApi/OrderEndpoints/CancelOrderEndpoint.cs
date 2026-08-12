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

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: cancels the order. The shopper is told, and any delivery follow-up not yet sent
/// is called off with the provider so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public CancelOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) => await HandleAsync(orderId, ct))
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var cancelled = await _orderNotificationService.CancelAsync(orderId, ct);
        if (!cancelled)
            return Results.NotFound();

        return Results.Ok(new CancelOrderResponse { OrderId = orderId, Status = "Cancelled" });
    }
}
