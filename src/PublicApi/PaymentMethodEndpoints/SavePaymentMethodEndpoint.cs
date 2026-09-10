using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — vaults a card for the signed-in shopper. The response identifies the saved
/// card and describes it safely (brand, last four, expiry) — never full card details. Returns the saved
/// card id as a top-level field.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodApiRequest request, ISavedCardService service, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var input = new SaveCardInput(request.Number, request.Expiry, request.SecurityCode,
                    request.Name, request.CountryCode, request.AddressLine1, request.AddressLine2,
                    request.AdminArea1, request.AdminArea2, request.PostalCode);

                var card = await service.SaveCardAsync(user.BuyerId(), input, ct);
                return Results.Created($"/api/payment-methods/{card.PaymentMethodId}", new
                {
                    paymentMethodId = card.PaymentMethodId,
                    brand = card.Brand,
                    lastDigits = card.LastDigits,
                    expiry = card.Expiry,
                    cardholderName = card.CardholderName
                });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
