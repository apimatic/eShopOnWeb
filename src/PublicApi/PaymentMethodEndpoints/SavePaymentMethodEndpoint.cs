using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it at PayPal. The response identifies the saved card and
/// describes it safely (brand, last four, expiry) — never full card details. Returns <c>paymentMethodId</c> at the top level.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (request?.Card == null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    return Results.BadRequest(new { error = "Card details are required to save a payment method." });
                }

                var method = await paymentMethodService.SaveCardAsync(buyerId, PaymentMapping.ToCardDetails(request.Card), request.Alias, cancellationToken);
                var dto = PaymentMapping.ToPaymentMethodDto(method);
                return Results.Created($"api/payment-methods/{method.Id}", new { paymentMethodId = method.Id, paymentMethod = dto });
            })
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }
}
