using System.Linq;
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
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (FinixPaymentService payments, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(payments, user, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        FinixPaymentService payments,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new ListPaymentMethodsResponse(System.Guid.NewGuid());
        var buyerId = ClaimsHelper.GetBuyerId(user);
        var cards = await payments.GetCardsAsync(buyerId, ct);
        response.PaymentMethods = cards.Select(c => c.ToDto()).ToList();
        return Results.Ok(response);
    }
}
