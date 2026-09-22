using System.Security.Claims;
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
/// What was sent for one of the signed-in shopper's orders, and what became of each message. Each entry
/// carries its own notificationId (what the operator endpoints act on).
/// </summary>
public class OrderNotificationsEndpoint
    : IEndpoint<IResult, OrderNotificationsRequest, IShopperOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IShopperOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(
                    new OrderNotificationsRequest { OrderId = orderId, BuyerId = user.Identity?.Name },
                    service, ct);
            })
            .Produces<OrderNotificationsView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IShopperOrderService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var view = await service.GetOrderNotificationsAsync(request.BuyerId!, request.OrderId, ct);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }
}

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
}
