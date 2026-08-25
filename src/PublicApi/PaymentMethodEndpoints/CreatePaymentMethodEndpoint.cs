using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. The full card number is
/// never stored locally or logged - only the PayPal vault token id and a display-safe summary.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest,
    (IRepository<SavedPaymentMethod> SavedCards, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IRepository<SavedPaymentMethod> savedCards, IPaymentGatewayService gateway,
             ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, (savedCards, gateway, user, ct));
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request,
        (IRepository<SavedPaymentMethod> SavedCards, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct) dependency)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is null)
        {
            return Results.BadRequest("card is required.");
        }

        var card = request.Card.ToCardDetails();
        var requestId = $"eshop-vault-{Guid.NewGuid()}";
        var vaultResult = await dependency.Gateway.SaveCardAsync(buyerId, card, requestId, dependency.Ct);

        var savedCard = new SavedPaymentMethod(buyerId, vaultResult.VaultId, vaultResult.CardBrand, vaultResult.Last4, vaultResult.Expiry, vaultResult.CardholderName);
        await dependency.SavedCards.AddAsync(savedCard);

        response.PaymentMethodId = savedCard.Id;
        response.CardBrand = savedCard.CardBrand;
        response.Last4 = savedCard.Last4;
        response.Expiry = savedCard.Expiry;

        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
