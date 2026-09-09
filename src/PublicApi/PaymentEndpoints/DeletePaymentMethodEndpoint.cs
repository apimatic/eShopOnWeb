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

/// <summary>Removes a saved card. Afterwards it no longer appears and can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int paymentMethodId, ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var removed = await paymentMethodService.DeleteAsync(buyerId, paymentMethodId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
