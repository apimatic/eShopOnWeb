using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the authenticated shopper's saved cards. Afterwards the card no longer appears in
/// the shopper's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http) => await HandleAsync(paymentMethodId, http))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, HttpContext http)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var paymentMethodService = http.RequestServices.GetRequiredService<IPaymentMethodService>();

        var deleted = await paymentMethodService.DeleteCardAsync(buyerId, paymentMethodId, http.RequestAborted);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
