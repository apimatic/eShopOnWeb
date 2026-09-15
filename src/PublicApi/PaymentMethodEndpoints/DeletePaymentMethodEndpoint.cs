using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes a saved card. Afterwards it no longer
/// appears among the caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, ISavedCardService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service, HttpContext ctx) =>
                await HandleAsync(service, ctx))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);
        var paymentMethodId = PaymentMapper.GetRouteInt(ctx, "paymentMethodId");
        await service.DeleteCardAsync(buyerId, paymentMethodId, ctx.RequestAborted);
        return Results.NoContent();
    }
}
