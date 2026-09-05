using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Maxio Advanced Billing subscription endpoints for authenticated shoppers.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionService service, UserManager<ApplicationUser> users, CancellationToken cancellationToken) =>
            {
                if (await CurrentUserAsync(context, users) is null) return Results.Unauthorized();
                return Results.Ok(new SubscriptionPlansResponse(await service.GetPlansAsync(cancellationToken)));
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext context, ISubscriptionService service, UserManager<ApplicationUser> users, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle)) return Results.BadRequest(new { message = "productHandle is required." });
                var user = await CurrentUserAsync(context, users);
                if (user is null) return Results.Unauthorized();
                return Results.Ok(new SubscribeResponse(await service.SubscribeAsync(user, request.ProductHandle, cancellationToken)));
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionService service, UserManager<ApplicationUser> users, CancellationToken cancellationToken) =>
            {
                var user = await CurrentUserAsync(context, users);
                if (user is null) return Results.Unauthorized();
                return Results.Ok(new MySubscriptionsResponse(await service.GetMySubscriptionsAsync(user, cancellationToken)));
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static Task<ApplicationUser?> CurrentUserAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        var username = context.User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(username) ? Task.FromResult<ApplicationUser?>(null) : users.FindByNameAsync(username);
    }

    // Routes are handled by the three endpoint-specific delegates registered above.
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());
}

public sealed class SubscribeRequest
{
    [Required]
    [StringLength(255)]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans);
public sealed record SubscribeResponse(SubscriptionDto Subscription);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
