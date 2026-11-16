using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: captures the hold and marks the order fulfilled.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, FinixPaymentService payments, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, payments, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, FinixPaymentService payments, CancellationToken ct = default)
    {
        var response = new FulfilOrderResponse(System.Guid.NewGuid());
        var order = await payments.FulfilAsync(orderId, ct);

        response.OrderId = order.Id;
        response.PaymentStatus = order.PaymentStatus.ToString();
        response.CaptureTransferId = order.CaptureTransferId;
        response.CaptureState = order.CaptureState;
        response.CapturedAmountCents = order.CapturedAmountCents;
        response.FeeCents = order.FinixFeeCents;
        response.NetCents = order.NetProceedsCents;
        response.FulfilledAt = order.FulfilledAt;

        return Results.Ok(response);
    }
}
