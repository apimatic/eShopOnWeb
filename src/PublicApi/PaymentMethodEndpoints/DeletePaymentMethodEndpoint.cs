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
/// Removes one of the signed-in shopper's saved cards. Afterwards the card no longer appears among
/// the shopper's cards and can no longer be used to pay. Only the owner can delete a card.
/// </summary>
public class DeletePaymentMethodEndpoint
    : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService savedCardService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var request = new DeletePaymentMethodRequest();
                request.SetRouteAndBuyer(paymentMethodId, buyerId);
                return await HandleAsync(request, savedCardService, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService savedCardService,
        CancellationToken ct)
    {
        var result = await savedCardService.DeleteCardAsync(request.BuyerId!, request.PaymentMethodId, ct);

        var failure = ApiResultMapper.MapFailure(result);
        if (failure is not null)
        {
            return failure;
        }

        return Results.NoContent();
    }
}
