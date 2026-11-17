using System.Linq;
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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly FinixPaymentService _payments;

    public ListPaymentMethodsEndpoint(FinixPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var cards = await _payments.GetSavedCardsAsync(buyerId, CancellationToken.None);
        var response = new PaymentMethodsResponse
        {
            PaymentMethods = cards
                .Select(PaymentResponseMapper.ToSavedPaymentMethodResponse)
                .ToList()
        };

        return Results.Ok(response);
    }
}
