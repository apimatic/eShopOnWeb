using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — vaults a card with PayPal and saves a safe reference for the shopper.
/// Full card details are never stored in the application database.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreatePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                CardDetails card = CardMapping.ToCardDetails(request.Card);

                var paymentMethod = await savedCardService.SaveCardAsync(
                    buyerId, card, request.Alias, cancellationToken);

                var response = new CreatePaymentMethodResponse
                {
                    PaymentMethodId = paymentMethod.Id,
                    PaymentMethod = SavedCardDto.FromEntity(paymentMethod)
                };
                return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
