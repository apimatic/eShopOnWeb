using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, at the processor as well as here — afterwards it neither
/// appears in their list nor works to pay. Another shopper's card is simply not found.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IPaymentMethodService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService paymentMethodService, HttpContext context) =>
            {
                return await HandleAsync(paymentMethodId, paymentMethodService, context);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, IPaymentMethodService paymentMethodService,
        HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var deleted = await paymentMethodService.DeleteAsync(buyerId, paymentMethodId, context.RequestAborted);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
