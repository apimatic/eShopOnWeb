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
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled and the held funds are
/// captured (taken). A stale authorization is renewed automatically before capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var payment = await service.FulfilAsync(orderId, ct);
                return Results.Ok(PaymentMappings.ToState(payment));
            }))
            .Produces<PaymentStateResponse>()
            .WithTags("Payments");
    }
}
