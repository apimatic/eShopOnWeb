using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. Returns a safe description
/// and <c>paymentMethodId</c> as a top-level field. Full card details are never stored.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ISavedCardService service,
                HttpContext http,
                System.Threading.CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (request.Card is null)
                    throw new PaymentValidationException("Card details are required to save a payment method.");

                var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Label, ct);
                var response = PaymentMethodResponse.From(saved);
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
