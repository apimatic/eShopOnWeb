using System.Security.Claims;
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
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, PaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, PaymentService payments, ClaimsPrincipal user) =>
            {
                return await HandleAsync(paymentMethodId, payments, user);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, PaymentService payments, ClaimsPrincipal user)
    {
        var owner = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Results.Unauthorized();
        }

        var response = await payments.DeletePaymentMethodAsync(owner, paymentMethodId, default);
        return Results.Ok(response);
    }
}
