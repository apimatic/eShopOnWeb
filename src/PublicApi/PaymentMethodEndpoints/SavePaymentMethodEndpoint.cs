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
/// Saves (vaults) a card for the signed-in shopper for reuse on later orders. The response identifies
/// the saved card and describes it safely — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint
    : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Card is null)
                {
                    return Results.BadRequest(new { errors = new[] { "A 'card' is required." } });
                }

                request.SetBuyer(buyerId);
                return await HandleAsync(request, savedCardService, ct);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService,
        CancellationToken ct)
    {
        var saved = await savedCardService.SaveCardAsync(request.BuyerId!, request.Card!.ToCardDetails(), ct);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = SavedCardDto.From(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
