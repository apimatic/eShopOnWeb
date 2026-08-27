using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized money.
/// A stale authorization is renewed first; one that cannot be renewed returns the
/// order to awaiting-payment with an actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var payment = await orderPaymentService.FulfilOrderAsync(orderId, ct);
                if (payment is null)
                {
                    return Results.NotFound();
                }

                var response = new FulfilOrderResponse
                {
                    OrderId = orderId,
                    Status = "Fulfilled",
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
