using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order from catalog items for the signed-in shopper.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await PaymentProblem.RunAsync(async () =>
                {
                    var orderId = await paymentService.PlaceOrderAsync(buyerId, request.Items, request.ShipToAddress, ct);
                    return Results.Created($"api/orders/{orderId}", new
                    {
                        orderId,
                        status = "AwaitingPayment",
                    });
                });
            })
            .WithTags("PaymentEndpoints");
    }
}
