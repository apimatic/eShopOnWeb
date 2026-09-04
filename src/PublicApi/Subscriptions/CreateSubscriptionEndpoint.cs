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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(SubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, ClaimsPrincipal principal,
                CancellationToken cancellationToken) => await HandleAsync(request, principal, cancellationToken))
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request) =>
        HandleAsync(request, new ClaimsPrincipal(), CancellationToken.None);

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { error = "planHandle is required." });

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            return Results.Unauthorized();

        var subscription = await _subscriptionService.SubscribeAsync(user, request.PlanHandle, cancellationToken);
        if (subscription is null)
            return Results.NotFound(new { error = "The requested subscription plan was not found." });

        return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription });
    }
}
