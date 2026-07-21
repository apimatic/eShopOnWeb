using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Populated server-side from the authenticated principal - never bound from client input.</summary>
    internal string CustomerReference { get; set; } = string.Empty;
    internal string Email { get; set; } = string.Empty;
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public SubscriptionDto Subscription { get; set; } = null!;
    public bool WasAlreadyEnrolled { get; set; }
}

/// <summary>
/// Enrolls the authenticated user in a plan (UC1 hero flow). Idempotent - a repeat call while
/// already enrolled in the same plan returns the existing subscription rather than double-enrolling.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.CustomerReference = user.FindFirstValue(ClaimTypes.Name)!;
                // eShopOnWeb's ASP.NET Core Identity username is itself the email address, and the
                // issued JWT carries only ClaimTypes.Name/Role (see IdentityTokenClaimService) - no
                // separate email claim. Use the reference directly when it already looks like an
                // email; only synthesize a placeholder for the (rare) non-email username case.
                request.Email = user.FindFirstValue(ClaimTypes.Email)
                    ?? (request.CustomerReference.Contains('@') ? request.CustomerReference : $"{request.CustomerReference}@eshoponweb.local");
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            request.CustomerReference,
            request.Email,
            request.CustomerReference,
            request.CustomerReference,
            request.PlanHandle);

        response.Subscription = SubscriptionDto.FromDomain(result.Subscription);
        response.WasAlreadyEnrolled = result.WasAlreadyEnrolled;

        return Results.Ok(response);
    }
}
