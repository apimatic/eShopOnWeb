using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment and releases any held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var payment = await paymentService.CancelOrderAsync(orderId, cancellationToken);
                var response = new CancelOrderResponse(System.Guid.NewGuid())
                {
                    OrderId = payment.OrderId,
                    PaymentStatus = payment.Status.ToString(),
                    AuthorizationState = payment.AuthorizationState,
                    Released = payment.AuthorizationId is not null
                };

                return Results.Ok(response);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
