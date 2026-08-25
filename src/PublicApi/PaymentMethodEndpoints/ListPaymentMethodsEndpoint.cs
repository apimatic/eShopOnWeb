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

/// <summary>Returns the caller's saved payment methods.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, EmptyPmRequest, IRepository<PaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<PaymentMethod> pmRepo,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = GetBuyerId(user);
                var methods = await pmRepo.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), ct);
                var response = methods.Select(pm =>
                    new PaymentMethodDto(pm.Id, pm.LastFour, pm.Brand, pm.Expiry, pm.CardholderName, pm.CreatedAt)).ToList();
                return Results.Ok(response);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyPmRequest request, IRepository<PaymentMethod> repo)
        => throw new System.NotImplementedException();

    private static string GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue("sub")
        ?? user.Identity?.Name
        ?? throw new System.UnauthorizedAccessException();
}

public record EmptyPmRequest();
public record PaymentMethodDto(int PaymentMethodId, string? LastFour, string? Brand, string? Expiry, string? Name, System.DateTimeOffset CreatedAt);
