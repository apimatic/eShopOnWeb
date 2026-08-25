using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, string, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string paymentMethodId, IPaymentService svc, IHttpContextAccessor ctx) =>
                await HandleAsync(paymentMethodId, svc, ctx))
            .Produces(204)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(string paymentMethodId, IPaymentService svc, IHttpContextAccessor ctx)
    {
        var shopperId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        await svc.DeleteSavedCardAsync(shopperId, paymentMethodId);
        return Results.NoContent();
    }
}
