using System;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the caller's
/// saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Delete a saved card", Tags = new[] { "PaymentMethods" })]
        async (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(paymentMethodId, user, service, ct))
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        try
        {
            var removed = await service.DeleteAsync(buyerId, paymentMethodId, ct);
            return removed
                ? Results.NoContent()
                : Results.NotFound(new { message = "The requested saved card was not found for this shopper." });
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
