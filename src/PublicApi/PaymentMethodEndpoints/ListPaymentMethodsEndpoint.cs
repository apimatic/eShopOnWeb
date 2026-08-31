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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// The signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService savedPaymentMethodService, CancellationToken ct) =>
            {
                return await HandleAsync(user, savedPaymentMethodService, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISavedPaymentMethodService savedPaymentMethodService, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await savedPaymentMethodService.ListAsync(buyerId, ct);

        var response = new ListPaymentMethodsResponse();
        response.PaymentMethods.AddRange(methods.Select(m => new PaymentMethodDto
        {
            PaymentMethodId = m.Id,
            Brand = m.Brand,
            LastDigits = m.LastDigits,
            Expiry = m.Expiry,
            CreatedAt = m.CreatedAt
        }));

        return Results.Ok(response);
    }
}
