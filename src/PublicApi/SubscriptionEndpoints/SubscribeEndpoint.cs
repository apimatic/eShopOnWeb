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

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    public string? ResolvedHandle => !string.IsNullOrWhiteSpace(PlanHandle) ? PlanHandle : Handle;

    /// <summary>Filled from the JWT, never from the body.</summary>
    internal string UserName { get; set; } = string.Empty;
}

/// <summary>
/// Subscribe the signed-in shopper to a plan (no card details needed)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, SubscriptionBilling billing, HttpContext http) =>
            {
                request.UserName = Caller.UserName(user);
                return await HandleAsync(request, billing, http.RequestAborted);
            })
            .Produces<SubscriptionView>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionBilling billing)
        => HandleAsync(request, billing, default);

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionBilling billing, System.Threading.CancellationToken ct)
    {
        var handle = request.ResolvedHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw MaxioProblemException.BadRequest("planHandle is required.");

        var view = await billing.SubscribeAsync(request.UserName, handle, ct);
        return Results.Created($"api/subscriptions/{view.Id}", view);
    }
}
