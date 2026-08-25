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
/// Removes a saved card belonging to the signed-in shopper. Afterwards it no longer appears
/// among the caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest,
    (IRepository<SavedPaymentMethod> SavedCards, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IRepository<SavedPaymentMethod> savedCards, IPaymentGatewayService gateway,
             ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), (savedCards, gateway, user, ct));
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request,
        (IRepository<SavedPaymentMethod> SavedCards, IPaymentGatewayService Gateway, ClaimsPrincipal User, CancellationToken Ct) dependency)
    {
        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = await dependency.SavedCards.GetByIdAsync(request.PaymentMethodId);
        if (card is null || card.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await dependency.Gateway.DeleteVaultedCardAsync(card.VaultId, dependency.Ct);
        await dependency.SavedCards.DeleteAsync(card);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId()));
    }
}
