using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Save a card for the signed-in shopper. Only a Finix token and safe display metadata are stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<SavedPaymentMethodView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request,
        ISavedPaymentMethodService paymentMethodService)
    {
        var card = request.Card?.ToInput() ?? new CardInput
        {
            Number = request.Number,
            ExpirationMonth = request.ResolveExpirationMonth(),
            ExpirationYear = request.ResolveExpirationYear(),
            SecurityCode = request.ResolveSecurityCode(),
            Name = request.Name,
            Address = request.Address?.ToInput()
        };

        var saved = await paymentMethodService.SaveAsync(request.BuyerId ?? string.Empty, card);
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
    }
}
