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
/// Operator action: fulfils the order, which is when the money is actually taken (the authorization
/// is captured). A stale hold is renewed first. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var payment = await paymentService.FulfilAsync(orderId, ct);

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
