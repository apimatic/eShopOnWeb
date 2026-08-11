using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment, voiding the hold so
/// no money moves. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                var payment = await service.CancelAsync(orderId, ct);
                return Results.Ok(PaymentMapping.ToStateDto(payment));
            })
            .Produces<PaymentStateDto>()
            .WithTags("OrderPaymentEndpoints");
    }
}
