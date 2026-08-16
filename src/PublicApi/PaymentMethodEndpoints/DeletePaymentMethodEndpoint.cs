using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record DeletePaymentMethodCommand(int PaymentMethodId);

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes a saved card. Afterwards it no longer
/// appears among the caller's cards and can no longer be used to pay (it is removed from PayPal's
/// vault too). Shopper-scoped: one shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodCommand, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService) =>
                await HandleAsync(new DeletePaymentMethodCommand(paymentMethodId), savedCardService))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodCommand command, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        var deleted = await savedCardService.DeleteAsync(buyerId, command.PaymentMethodId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
