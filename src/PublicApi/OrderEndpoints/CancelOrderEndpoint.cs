using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>What the shopper gets back: everything charged, the express fee included.</summary>
    public decimal Refunded { get; set; }
}

/// <summary>
/// Cancels an order paid by bank, before it ships, and gives the money back
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, OrderCancellationService cancellation, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, user, cancellation, cancellationToken);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, OrderCancellationService cancellation,
        CancellationToken cancellationToken)
    {
        var result = await cancellation.CancelAsync(user.ShopperId(), orderId, cancellationToken);
        return Results.Ok(new CancelOrderResponse { OrderId = result.OrderId, Status = result.Status, Refunded = result.Refunded });
    }
}
