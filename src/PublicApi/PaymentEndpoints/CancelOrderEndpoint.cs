using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: cancels an unfulfilled order and releases the hold.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, FinixPaymentService payments, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, payments, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, FinixPaymentService payments, CancellationToken ct = default)
    {
        var response = new CancelOrderResponse(System.Guid.NewGuid());
        var order = await payments.CancelAsync(orderId, ct);

        response.OrderId = order.Id;
        response.PaymentStatus = order.PaymentStatus.ToString();
        response.AuthorizationId = order.FinixAuthorizationId;
        response.AuthorizationState = order.AuthorizationState;
        response.IsVoid = order.PaymentStatus == PaymentStatus.Cancelled;
        response.CancelledAt = order.CancelledAt;

        return Results.Ok(response);
    }
}
