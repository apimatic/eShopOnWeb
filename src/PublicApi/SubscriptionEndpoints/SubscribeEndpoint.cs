using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Subscribes the caller to a plan, without card details
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, billing, ct);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct)
    {
        var subscription = await billing.SubscribeAsync(user.Identity?.Name ?? string.Empty, request.PlanHandle, ct);
        return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
    }
}
