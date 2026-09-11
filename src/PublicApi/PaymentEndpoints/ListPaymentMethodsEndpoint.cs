using System.Collections.Generic;
using System.Linq;
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

/// <summary>Lists the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "List the caller's saved cards", Tags = new[] { "PaymentMethods" })]
        async (ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(user, service, ct))
            .Produces<IReadOnlyList<PaymentMethodResponse>>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var cards = await service.ListAsync(buyerId, ct);
        var response = cards
            .Select(c => new PaymentMethodResponse(c.Id, c.CardBrand, c.LastDigits, c.Expiry, c.CardholderName, c.CreatedAt))
            .ToList();
        return Results.Ok(response);
    }
}
