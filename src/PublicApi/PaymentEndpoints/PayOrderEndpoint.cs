using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Finix;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total without capturing it. Accepts either card details for a
/// one-off payment or the id of one of the caller's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, FinixPaymentService payments,
                CancellationToken ct) =>
            {
                var shopperId = user.Identity?.Name ?? string.Empty;
                var response = await payments.PayOrderAsync(shopperId, orderId, request, ct);
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPayments");
    }
}
