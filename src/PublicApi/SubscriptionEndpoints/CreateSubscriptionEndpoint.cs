using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Enrolls the authenticated shopper in a Maxio subscription plan.</summary>
public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal principal, IMaxioSubscriptionService subscriptions,
                UserManager<ApplicationUser> users, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle)) return Results.BadRequest(new { message = "planHandle is required." });
                var user = await FindUserAsync(principal, users);
                if (user is null) return Results.Unauthorized();
                var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                return Results.Ok(subscription);
            })
            .Produces<SubscriptionSummary>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    internal static Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(username) ? Task.FromResult<ApplicationUser?>(null) : users.FindByNameAsync(username);
    }
}

public sealed class CreateSubscriptionRequest
{
    [Required]
    public string PlanHandle { get; init; } = string.Empty;
}
