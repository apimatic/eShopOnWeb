using System.Security.Claims;
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
/// POST /api/payment-methods — saves a card for the signed-in shopper (vaulted at PayPal). The response
/// identifies the saved card and describes it safely (brand, last four, expiry) — never full details.
/// Returns the saved card's id as a top-level field.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                var command = new SaveCardCommand(
                    request.Number, request.Expiry, request.SecurityCode, request.CardholderName,
                    request.AddressLine1, request.AddressLine2, request.City, request.State,
                    request.PostalCode, request.CountryCode);

                var card = await paymentMethodService.SaveCardAsync(buyerId, command, cancellationToken);
                var dto = SavedCardDto.From(card);

                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new
                {
                    paymentMethodId = dto.PaymentMethodId,
                    brand = dto.Brand,
                    last4 = dto.Last4,
                    expiry = dto.Expiry,
                    cardholderName = dto.CardholderName
                });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
