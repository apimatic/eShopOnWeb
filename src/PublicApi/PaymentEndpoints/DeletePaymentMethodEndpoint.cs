using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove one of the caller's saved cards, deleting the
/// PayPal vault token so it can no longer be used to pay. Shopper-scoped: one shopper can never delete
/// another's card (a card that is not the caller's returns 404).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, (string BuyerId, int PaymentMethodId), ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, ISavedCardService service) =>
                await HandleAsync((user.GetBuyerId(), paymentMethodId), service))
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync((string BuyerId, int PaymentMethodId) request, ISavedCardService service)
    {
        var deleted = await service.DeleteCardAsync(request.BuyerId, request.PaymentMethodId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
