using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. The card is deleted from PayPal's vault and
/// from the local store, after which it no longer appears in the shopper's cards and can no longer
/// be used to pay. Scoped to the owner, so one shopper cannot delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http) => await HandleAsync(http))
            .Produces<DeletePaymentMethodResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var paymentMethodId = http.GetRouteInt("paymentMethodId");
        if (paymentMethodId is null)
        {
            return Results.BadRequest("A valid payment method id is required.");
        }

        var repository = http.RequestServices.GetRequiredService<IRepository<SavedPaymentMethod>>();
        var gateway = http.RequestServices.GetRequiredService<IPaymentGatewayService>();

        var card = await repository.FirstOrDefaultAsync(
            new PaymentMethodByIdAndBuyerSpecification(paymentMethodId.Value, buyerId));
        if (card is null)
        {
            // 404 for both missing and not-owned so one shopper cannot probe another's cards.
            return Results.NotFound($"Saved card {paymentMethodId} was not found.");
        }

        try
        {
            await gateway.DeleteVaultedCardAsync(card.PaymentTokenId, http.RequestAborted);
        }
        catch (PaymentGatewayException ex)
        {
            // Keep the local record so state stays consistent and the caller can retry.
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Could not delete card");
        }

        await repository.DeleteAsync(card);

        return Results.Ok(new DeletePaymentMethodResponse());
    }
}
