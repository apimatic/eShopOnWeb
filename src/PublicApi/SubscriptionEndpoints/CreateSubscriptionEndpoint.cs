using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    private const int MaxHandleLength = 100;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptionService, userManager, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle) || planHandle.Length > MaxHandleLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { $"PlanHandle is required (max {MaxHandleLength} characters)." }
            });
        }

        var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
        if (subscriber is null) return Results.Unauthorized();

        var result = await subscriptionService.SubscribeAsync(subscriber, planHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            Created = result.Created,
        };

        // 201 for a new subscription, 200 when the user was already subscribed (idempotent replay).
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
