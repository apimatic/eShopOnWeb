using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among their
/// cards and can no longer be used to pay. Only the owner may remove a card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, HttpContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, IPaymentMethodService service) =>
                await HandleAsync(paymentMethodId, http, service))
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, HttpContext http, IPaymentMethodService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var removed = await service.DeleteAsync(buyerId, paymentMethodId, http.RequestAborted);
            return removed ? Results.NoContent() : Results.NotFound();
        });
}
