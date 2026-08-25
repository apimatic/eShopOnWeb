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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class GetPaymentMethodsEndpoint : IEndpoint<IResult, string, IReadRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<SavedPaymentMethod> repo, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var userId = user.Identity?.Name ?? "";
                return await HandleAsync(userId, repo, ct);
            })
            .Produces<GetPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string userId, IReadRepository<SavedPaymentMethod> repo)
        => await HandleAsync(userId, repo, default);

    private async Task<IResult> HandleAsync(string userId, IReadRepository<SavedPaymentMethod> repo, CancellationToken ct)
    {
        var spec = new SavedPaymentMethodsByUserSpec(userId);
        var methods = await repo.ListAsync(spec, ct);

        return Results.Ok(new GetPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.Id,
                Last4Digits = m.Last4Digits,
                CardBrand = m.CardBrand,
                Expiry = m.Expiry
            }).ToList()
        });
    }
}
