using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
/// POST /api/payment-methods — saves a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details.
/// Returns the saved card id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreatePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    throw new PaymentException("Card details are required to save a payment method.");
                }

                var buyerId = user.GetBuyerId();
                var saved = await paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), cancellationToken);

                var dto = SavedCardDto.From(saved);
                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}",
                    new CreatePaymentMethodResponse { PaymentMethodId = dto.PaymentMethodId, Card = dto });
            }))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
