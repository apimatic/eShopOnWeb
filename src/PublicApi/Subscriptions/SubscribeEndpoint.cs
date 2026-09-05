using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Creates an enrollment for an active plan, idempotently per user and product handle.</summary>
public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/subscriptions", async (HttpContext context, SubscribeRequest request,
            IMaxioBillingClient maxio, UserManager<ApplicationUser> userManager,
            AppIdentityDbContext identityContext, CancellationToken cancellationToken) =>
                await HandleAsync(context, request, maxio, userManager, identityContext, cancellationToken))
            .RequireAuthorization()
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(HttpContext context, SubscribeRequest request,
        IMaxioBillingClient maxio, UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "A productHandle is required." }
            });
        }

        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        var productHandle = request.ProductHandle.Trim();
        try
        {
            var plan = (await maxio.GetPlansAsync(cancellationToken)).SingleOrDefault(x =>
                string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
                {
                    [nameof(request.ProductHandle)] = new[] { "The requested plan is not available." }
                });
            }

            var enrollmentGate = await SubscriptionEndpointHelpers.EnterEnrollmentGateAsync(user.Id, productHandle, cancellationToken);
            try
            {
                var (enrollment, wasCreated) = await SubscriptionEndpointHelpers.GetOrCreateEnrollmentAsync(
                    identityContext, user.Id, productHandle, cancellationToken);

                var customer = await maxio.GetOrCreateCustomerAsync(SubscriptionEndpointHelpers.CustomerInput(user), cancellationToken);
                var existing = SubscriptionEndpointHelpers.FindPlanSubscription(
                    await maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken), productHandle);
                if (existing is not null)
                {
                    await SubscriptionEndpointHelpers.MarkCompleteAsync(identityContext, enrollment, existing.Id, cancellationToken);
                    return Results.Ok(SubscriptionEndpointHelpers.ToResponse(existing));
                }

                // An enrollment already being processed on another app instance must not issue a second Maxio POST.
                if (!wasCreated && string.Equals(enrollment.Status, "Pending", StringComparison.Ordinal))
                {
                    return Results.Conflict(new { detail = "An enrollment is already being processed. Refresh my-subscriptions shortly." });
                }

                enrollment.Status = "Pending";
                enrollment.UpdatedAt = DateTimeOffset.UtcNow;
                await identityContext.SaveChangesAsync(cancellationToken);

                var subscription = await maxio.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                await SubscriptionEndpointHelpers.MarkCompleteAsync(identityContext, enrollment, subscription.Id, cancellationToken);
                return Results.Created("/api/my-subscriptions", SubscriptionEndpointHelpers.ToResponse(subscription));
            }
            finally
            {
                enrollmentGate.Release();
            }
        }
        catch (MaxioApiException)
        {
            // If Maxio accepted the request but the response was lost, the next request first lists Maxio
            // subscriptions and will reconcile it before any further create operation.
            var trackedEnrollment = await identityContext.SubscriptionEnrollments
                .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == productHandle, cancellationToken);
            if (trackedEnrollment is not null)
            {
                await SubscriptionEndpointHelpers.MarkFailedAsync(identityContext, trackedEnrollment, cancellationToken);
            }

            return Results.Problem("The billing service could not complete the enrollment.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // The route handler supplies the authenticated user and identity context; this member satisfies
    // the endpoint discovery contract used by the existing MinimalApi.Endpoint package.
    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingClient maxio) =>
        Task.FromResult<IResult>(Results.Unauthorized());
}
