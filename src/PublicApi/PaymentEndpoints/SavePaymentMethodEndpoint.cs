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

/// <summary>POST /api/payment-methods — save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = new CardInput(request.Number, request.Expiry, request.SecurityCode,
                    request.CardholderName, request.BillingAddress);

                return await PaymentProblem.RunAsync(async () =>
                {
                    var saved = await paymentService.SaveCardAsync(buyerId, card, ct);
                    return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new
                    {
                        paymentMethodId = saved.PaymentMethodId,
                        brand = saved.Brand,
                        lastDigits = saved.LastDigits,
                        expiry = saved.Expiry,
                        cardholderName = saved.CardholderName,
                        descriptor = saved.Descriptor,
                    });
                });
            })
            .WithTags("PaymentEndpoints");
    }
}
