using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, string, ClaimsPrincipal>
{
    private readonly FinixPaymentService _payments;

    public DeletePaymentMethodEndpoint(FinixPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (string paymentMethodId, ClaimsPrincipal user) => await HandleAsync(paymentMethodId, user))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string paymentMethodId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var deleted = await _payments.DeleteSavedCardAsync(
            buyerId,
            paymentMethodId,
            CancellationToken.None);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
