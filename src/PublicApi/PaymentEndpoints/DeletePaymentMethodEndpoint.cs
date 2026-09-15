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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes one of the caller's saved cards. After
/// this it no longer appears among the caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int paymentMethodId,
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var buyerId = user.GetBuyerId();
                var deleted = await paymentMethodService.DeleteCardAsync(buyerId, paymentMethodId, cancellationToken);
                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(new { error = $"Saved card {paymentMethodId} was not found." });
            }))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }
}
