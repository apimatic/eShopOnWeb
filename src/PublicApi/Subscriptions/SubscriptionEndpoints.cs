using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionAuthorization
{
    public static readonly AuthorizeAttribute Jwt = new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
    };
}

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                async (ISubscriptionService service) => await HandleAsync(service))
            .RequireAuthorization(SubscriptionAuthorization.Jwt)
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service)
    {
        return Results.Ok(new SubscriptionPlansResponse(await service.GetPlansAsync(
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None)));
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                async (CreateSubscriptionRequest request, ISubscriptionService service,
                    UserManager<ApplicationUser> userManager) => await HandleAsync(request, service, userManager))
            .RequireAuthorization(SubscriptionAuthorization.Jwt)
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service,
        UserManager<ApplicationUser> userManager)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { error = "planHandle is required." });

        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;
        var user = await FindAuthenticatedUserAsync(userManager, httpContext?.User.Identity?.Name);
        if (user is null)
            return Results.Unauthorized();

        var subscription = await service.SubscribeAsync(user, request.PlanHandle.Trim(), cancellationToken);
        return Results.Created("api/my-subscriptions", new SubscriptionResponse(subscription));
    }

    private static Task<ApplicationUser?> FindAuthenticatedUserAsync(UserManager<ApplicationUser> userManager, string? userName)
    {
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(userName);
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                async (ISubscriptionService service, UserManager<ApplicationUser> userManager) =>
                    await HandleAsync(service, userManager))
            .RequireAuthorization(SubscriptionAuthorization.Jwt)
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService service, UserManager<ApplicationUser> userManager)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
            return Results.Unauthorized();

        return Results.Ok(new MySubscriptionsResponse(
            await service.GetMySubscriptionsAsync(user, cancellationToken)));
    }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionPlansResponse(System.Collections.Generic.IReadOnlyList<SubscriptionPlan> Plans);
public sealed record SubscriptionResponse(SubscriptionDetails Subscription);
public sealed record MySubscriptionsResponse(System.Collections.Generic.IReadOnlyList<SubscriptionDetails> Subscriptions);
