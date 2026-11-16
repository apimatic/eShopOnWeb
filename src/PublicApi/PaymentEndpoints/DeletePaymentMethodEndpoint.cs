using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Removes one of the caller's saved cards and disables it at Finix.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, FinixPaymentService payments, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(paymentMethodId, payments, user, ct);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int paymentMethodId,
        FinixPaymentService payments,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new DeletePaymentMethodResponse(System.Guid.NewGuid())
        {
            PaymentMethodId = paymentMethodId
        };

        var buyerId = ClaimsHelper.GetBuyerId(user);
        await payments.DeleteCardAsync(buyerId, paymentMethodId, ct);
        response.Deleted = true;

        return Results.Ok(response);
    }
}
