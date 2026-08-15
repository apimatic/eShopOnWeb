using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among their saved cards
/// and can no longer be used to pay. Scoped to the owner.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Remove a saved card", Tags = new[] { "PaymentMethodEndpoints" })]
            async (int paymentMethodId, ISavedCardService savedCardService, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                await savedCardService.DeleteCardAsync(buyerId, paymentMethodId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }
}
