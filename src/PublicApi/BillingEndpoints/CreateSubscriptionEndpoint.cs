using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using System;
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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreateSubscriptionRequest
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Subscribes the caller to a plan without card details
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IBillingService billing,
             UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request?.PlanHandle))
                    throw BillingException.BadRequest("planHandle is required.");
                var name = BillingEndpointHelpers.CallerName(user);
                var email = (await userManager.FindByNameAsync(name))?.Email;
                var created = await billing.SubscribeAsync(name, email, request.PlanHandle.Trim(), ct);
                return Results.Created($"api/subscriptions/{created.Id}", created);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}
