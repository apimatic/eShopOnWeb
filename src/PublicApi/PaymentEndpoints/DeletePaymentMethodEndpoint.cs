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

/// <summary>Removes one of the caller's saved cards; afterwards it can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, PaymentMethodReference, ISavedCardService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user) =>
                await HandleAsync(new PaymentMethodReference(paymentMethodId), service, user))
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodReference request, ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);
        var deleted = await service.DeleteCardAsync(buyerId, request.PaymentMethodId, CancellationToken.None);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
