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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the
/// card safely — brand and last digits only, never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                if (request.Card is null ||
                    string.IsNullOrWhiteSpace(request.Card.Number) ||
                    string.IsNullOrWhiteSpace(request.Card.Expiry) ||
                    string.IsNullOrWhiteSpace(request.Card.SecurityCode))
                {
                    return Results.BadRequest("Card number, expiry and securityCode are required.");
                }

                var card = new CardDetails(
                    request.Card.Number,
                    request.Card.Expiry,
                    request.Card.SecurityCode!,
                    request.Card.Name,
                    request.Card.BillingAddress?.AddressLine1,
                    request.Card.BillingAddress?.City,
                    request.Card.BillingAddress?.State,
                    request.Card.BillingAddress?.PostalCode,
                    request.Card.BillingAddress?.CountryCode);

                var method = await orderPaymentService.SaveCardAsync(buyerId, card, ct);

                var response = new SavePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = method.Id,
                    Brand = method.Brand,
                    LastDigits = method.LastDigits,
                    Expiry = method.Expiry,
                    CardholderName = method.CardholderName
                };
                return Results.Created($"api/payment-methods/{method.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
