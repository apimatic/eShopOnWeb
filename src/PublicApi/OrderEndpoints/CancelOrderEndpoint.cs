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
/// Operator action: cancels the order before fulfilment, voiding the PayPal
/// authorization so the shopper's held funds are released.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var result = await orderPaymentService.CancelOrderAsync(orderId, ct);
                if (result is null)
                {
                    return Results.NotFound();
                }

                var (order, payment) = result.Value;
                var response = new CancelOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Payment = payment is null ? null : PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
