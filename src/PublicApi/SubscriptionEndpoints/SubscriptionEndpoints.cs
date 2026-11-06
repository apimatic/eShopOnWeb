using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanHandleRequest : BaseRequest
{
    /// <summary>Handle of the plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    public string? Resolve() => PlanHandle ?? Handle;
}

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class ListPlansEndpoint : IEndpoint<IResult, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (BillingService billing) =>
                await HandleAsync(billing))
            .Produces<System.Collections.Generic.List<PlanDto>>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(BillingService billing) =>
        BillingResults.RunAsync(async () => Results.Ok(await billing.ListPlansAsync(default)));
}

/// <summary>Subscribes the signed-in shopper to a plan, without card details.</summary>
public class SubscribeEndpoint : IEndpoint<IResult, PlanHandleRequest, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(request, user, billing))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
        {
            var created = await billing.SubscribeAsync(BillingService.RequireShopper(user), request.Resolve(), default);
            return Results.Created($"api/subscriptions/{created.Id}", created);
        });
}

/// <summary>Shows one of the shopper's subscriptions.</summary>
public class GetSubscriptionEndpoint : IEndpoint<IResult, long, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(subscriptionId, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.GetSubscriptionAsync(BillingService.RequireShopper(user), subscriptionId, default)));
}

public class PauseSubscriptionEndpoint : IEndpoint<IResult, long, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(subscriptionId, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.PauseAsync(BillingService.RequireShopper(user), subscriptionId, default)));
}

public class ResumeSubscriptionEndpoint : IEndpoint<IResult, long, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(subscriptionId, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.ResumeAsync(BillingService.RequireShopper(user), subscriptionId, default)));
}

public class CancelSubscriptionEndpoint : IEndpoint<IResult, long, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(subscriptionId, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.CancelAsync(BillingService.RequireShopper(user), subscriptionId, default)));
}

public class RestartSubscriptionEndpoint : IEndpoint<IResult, long, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                await HandleAsync(subscriptionId, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.RestartAsync(BillingService.RequireShopper(user), subscriptionId, default)));
}

/// <summary>Switches a subscription (including a cancelled one) to another plan.</summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint<IResult, long, PlanHandleRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:long}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, PlanHandleRequest request, HttpContext context) =>
                await HandleAsync(subscriptionId, request, context))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, PlanHandleRequest request, HttpContext context) =>
        BillingResults.RunAsync(async () =>
        {
            var billing = context.RequestServices.GetRequiredService<BillingService>();
            return Results.Ok(await billing.SwitchPlanAsync(BillingService.RequireShopper(context.User), subscriptionId, request.Resolve(), context.RequestAborted));
        });
}

/// <summary>Shows the shopper's metered usage on a single day.</summary>
public class GetSubscriptionUsageEndpoint : IEndpoint<IResult, long, string?, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:long}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, string? date, HttpContext context) =>
                await HandleAsync(subscriptionId, date, context))
            .Produces<UsageDayDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, string? date, HttpContext context) =>
        BillingResults.RunAsync(async () =>
        {
            var billing = context.RequestServices.GetRequiredService<BillingService>();
            return Results.Ok(await billing.GetUsageAsync(BillingService.RequireShopper(context.User), subscriptionId, date, context.RequestAborted));
        });
}
