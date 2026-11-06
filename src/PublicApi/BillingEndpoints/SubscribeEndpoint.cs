using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    internal string? ResolvedHandle => string.IsNullOrWhiteSpace(PlanHandle) ? Handle : PlanHandle;
}

/// <summary>
/// Subscribes the signed-in shopper to a plan, without card details
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, System.Security.Claims.ClaimsPrincipal user, MaxioBillingService billing) =>
            {
                return await HandleAsync(request, BillingEndpointHelpers.UserName(user), billing);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, string userName, MaxioBillingService billing)
    {
        var subscription = await billing.SubscribeAsync(userName, request.ResolvedHandle);
        return Results.Created($"api/subscriptions/{subscription.SubscriptionId}", subscription);
    }
}
