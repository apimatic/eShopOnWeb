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
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// carries only safe display attributes — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCardService;

    public CreatePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var validationError = request.Card?.Validate();
        if (request.Card is null || validationError is not null)
        {
            return Results.BadRequest(new { message = validationError ?? "card is required." });
        }

        var savedCard = await _savedCardService.SaveCardAsync(buyerId, request.Card.ToModel(), CancellationToken.None);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry,
            CreatedAt = savedCard.CreatedAt
        };
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
