using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public record SubscribeRequest(string? PlanHandle);

/// <summary>
/// Subscribes the signed-in shopper to a plan (no card details needed)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, MaxioGateway billing) =>
            {
                return await HandleAsync(request, user, billing);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, MaxioGateway billing)
    {
        var created = await billing.SubscribeAsync(user.Identity!.Name!, request.PlanHandle);
        return Results.Created($"api/subscriptions/{created.Id}", created);
    }
}
