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
/// POST /api/orders/{orderId}/cancel — operator action. Cancels an order before fulfilment, releasing
/// the shopper's held funds so no money ever moved. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var order = await paymentService.CancelAsync(orderId, cancellationToken);

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
