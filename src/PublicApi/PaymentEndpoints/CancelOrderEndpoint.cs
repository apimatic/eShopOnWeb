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
/// Operator action: cancels an order before fulfilment, voiding the authorization so the shopper's
/// held funds are released and no money ever moves. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var payment = await paymentService.CancelAsync(orderId, ct);

                var response = new OrderPaymentResponse
                {
                    OrderId = orderId,
                    Status = PaymentMapping.OrderStatus(payment),
                    Payment = PaymentMapping.ToPaymentDto(payment)
                };
                return Results.Ok(response);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }
}
