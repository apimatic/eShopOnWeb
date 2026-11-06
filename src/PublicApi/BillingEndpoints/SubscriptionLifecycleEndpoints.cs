using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Pauses one of the caller's subscriptions
/// </summary>
public class SubscriptionPauseEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            var response = await billing.Client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new PauseRequest { Hold = new AutoResume() },
            });
            return Results.Ok(BillingService.ToDto(response.Subscription!));
        });
    }
}

/// <summary>
/// Resumes one of the caller's paused subscriptions
/// </summary>
public class SubscriptionResumeEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            var response = await billing.Client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
            });
            return Results.Ok(BillingService.ToDto(response.Subscription!));
        });
    }
}

/// <summary>
/// Cancels one of the caller's subscriptions
/// </summary>
public class SubscriptionCancelEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            var response = await billing.Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest
                {
                    Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the shopper" },
                },
            });
            return Results.Ok(BillingService.ToDto(response.Subscription!));
        });
    }
}

/// <summary>
/// Restarts one of the caller's cancelled subscriptions
/// </summary>
public class SubscriptionRestartEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            var response = await billing.Client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
            });
            return Results.Ok(BillingService.ToDto(response.Subscription!));
        });
    }
}

/// <summary>
/// Switches one of the caller's subscriptions to another plan, cancelled ones included
/// </summary>
public class SubscriptionSwitchEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, request, user, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    private static Task<IResult> HandleAsync(int subscriptionId, PlanHandleRequest request, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            var subscription = await billing.GetOwnedSubscriptionAsync(subscriptionId, user);
            // Validate the target before touching the subscription so a bad request has no side effects.
            var plan = await billing.GetPlanByHandleAsync(request?.ResolvedHandle);

            var state = subscription.State;
            var cancelled = state == SubscriptionState.Canceled || state == SubscriptionState.TrialEnded;
            var paused = state == SubscriptionState.OnHold || state == SubscriptionState.Paused;
            var samePlan = subscription.Product?.Id == plan.Id;

            if (samePlan && !cancelled)
                throw BillingException.Conflict($"The subscription is already on plan '{plan.Handle}'.");

            var client = billing.Client;
            if (cancelled)
            {
                // Maxio can't migrate a cancelled subscription, so bring it back first.
                var restarted = await client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                });
                subscription = restarted.Subscription!;
                if (samePlan) return Results.Ok(BillingService.ToDto(subscription));
            }
            else if (paused)
            {
                var resumed = await client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId });
                subscription = resumed.Subscription!;
            }

            var migrated = await client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = subscriptionId,
                Body = new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductId = plan.Id },
                },
            });
            subscription = migrated.Subscription!;

            if (paused)
            {
                // The shopper had paused it; switching plans does not un-pause it.
                var held = await client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new PauseRequest { Hold = new AutoResume() },
                });
                subscription = held.Subscription!;
            }

            return Results.Ok(BillingService.ToDto(subscription));
        });
    }
}
