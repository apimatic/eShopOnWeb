using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the
/// held funds; afterwards the payment shows PayPal's captured amount, fee and net. A stale hold is
/// renewed rather than failing the fulfilment. Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var order = await paymentService.FulfilAsync(orderId, cancellationToken);

                return Results.Ok(new
                {
                    orderId = order.Id,
                    status = order.Status.ToString(),
                    order = order.ToDto()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }
}
