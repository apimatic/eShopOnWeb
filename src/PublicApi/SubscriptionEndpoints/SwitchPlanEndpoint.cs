using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SwitchPlanRequest : BaseRequest
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    public string? ResolvedHandle => !string.IsNullOrWhiteSpace(PlanHandle) ? PlanHandle : Handle;

    internal string UserName { get; set; } = string.Empty;
    internal int SubscriptionId { get; set; }
}

/// <summary>
/// Switch one of the caller's subscriptions (cancelled ones included) to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint<IResult, SwitchPlanRequest, SubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, SubscriptionBilling billing, HttpContext http) =>
            {
                request.UserName = Caller.UserName(user);
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, billing, http.RequestAborted);
            })
            .Produces<SubscriptionView>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SwitchPlanRequest request, SubscriptionBilling billing)
        => HandleAsync(request, billing, default);

    public async Task<IResult> HandleAsync(SwitchPlanRequest request, SubscriptionBilling billing, System.Threading.CancellationToken ct)
    {
        var handle = request.ResolvedHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw MaxioProblemException.BadRequest("planHandle is required.");

        var view = await billing.SwitchPlanAsync(request.UserName, request.SubscriptionId, handle, ct);
        return Results.Ok(view);
    }
}
