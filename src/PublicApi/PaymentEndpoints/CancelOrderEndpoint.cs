using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; the held funds are
/// released, so no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var payment = await service.CancelAsync(orderId, ct);
                return Results.Ok(PaymentMappings.ToState(payment));
            }))
            .Produces<PaymentStateResponse>()
            .WithTags("Payments");
    }
}
