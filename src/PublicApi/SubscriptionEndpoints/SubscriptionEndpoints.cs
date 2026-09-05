using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists plans and creates or reads subscriptions held by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioClient>
{
    // Routes are registered together in AddRoute; this member fulfills the endpoint-discovery contract.
    public Task<IResult> HandleAsync(IMaxioClient maxio) => Task.FromResult<IResult>(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxio, CancellationToken cancellationToken) =>
            {
                var plans = await maxio.ListPlansAsync(cancellationToken);
                return (IResult)Results.Ok(new SubscriptionPlansResponse(plans.Select(SubscriptionPlanDto.From).ToList()));
            })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                AppIdentityDbContext identityDb, IMaxioClient maxio, SubscriptionEnrollmentCoordinator coordinator,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return (IResult)Results.BadRequest(new { error = "productHandle is required." });
                }

                var user = await FindCurrentUserAsync(principal, users);
                if (user is null)
                {
                    return (IResult)Results.Unauthorized();
                }

                var email = user.Email ?? user.UserName;
                if (string.IsNullOrWhiteSpace(email))
                {
                    return (IResult)Results.BadRequest(new { error = "The signed-in user must have an email address to subscribe." });
                }

                return await coordinator.RunAsync<IResult>($"{user.Id}:{request.ProductHandle}", async () =>
                {
                    MaxioSubscriptionEnrollment? enrollment = null;
                    try
                    {
                        var plans = await maxio.ListPlansAsync(cancellationToken);
                        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, request.ProductHandle, StringComparison.Ordinal));
                        if (plan is null)
                        {
                            return Results.BadRequest(new { error = "The requested subscription plan is not available." });
                        }

                        enrollment = await identityDb.MaxioSubscriptionEnrollments
                            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == plan.Handle, cancellationToken);
                        var ownsReservation = enrollment is null;
                        if (ownsReservation)
                        {
                            enrollment = new MaxioSubscriptionEnrollment
                            {
                                UserId = user.Id,
                                ProductHandle = plan.Handle,
                                ProvisioningStartedAt = DateTimeOffset.UtcNow,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            identityDb.MaxioSubscriptionEnrollments.Add(enrollment);
                            try
                            {
                                await identityDb.SaveChangesAsync(cancellationToken);
                            }
                            catch (DbUpdateException)
                            {
                                identityDb.Entry(enrollment).State = EntityState.Detached;
                                enrollment = await identityDb.MaxioSubscriptionEnrollments.SingleAsync(
                                    x => x.UserId == user.Id && x.ProductHandle == plan.Handle, cancellationToken);
                                ownsReservation = false;
                            }
                        }

                        var customerReference = CustomerReference(user.Id);
                        var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
                        var customer = await maxio.EnsureCustomerAsync(customerReference, email, CustomerFirstName(email), "Shopper", cancellationToken);
                        var existing = (await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                            .SingleOrDefault(x => string.Equals(x.Reference, subscriptionReference, StringComparison.Ordinal));

                        if (existing is not null)
                        {
                            await StoreSubscriptionIdAsync(identityDb, enrollment!, existing.Id, cancellationToken);
                            return Results.Ok(new CreateSubscriptionResponse(SubscriptionDto.From(existing, plan), false));
                        }

                        // A different app instance holds the durable reservation. It must be the only creator
                        // while its lease is fresh. An expired lease is recoverable after a crash.
                        if (!ownsReservation)
                        {
                            if (enrollment!.ProvisioningStartedAt is { } started && started > DateTimeOffset.UtcNow.AddMinutes(-2))
                            {
                                return Results.Conflict(new { error = "Subscription enrollment is already being processed. Retry shortly." });
                            }

                            enrollment.ProvisioningStartedAt = DateTimeOffset.UtcNow;
                            await identityDb.SaveChangesAsync(cancellationToken);
                            ownsReservation = true;
                        }

                        var created = await maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
                        await StoreSubscriptionIdAsync(identityDb, enrollment!, created.Id, cancellationToken);
                        return Results.Created($"api/subscriptions/{created.Id}", new CreateSubscriptionResponse(SubscriptionDto.From(created, plan), true));
                    }
                    catch (MaxioApiException ex)
                    {
                        await ClearProvisioningLeaseAsync(identityDb, enrollment, cancellationToken);
                        return MaxioProblem(ex);
                    }
                    catch (HttpRequestException)
                    {
                        return Results.Problem("Maxio could not be reached.", statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Results.Problem("Maxio did not respond in time. Retrying is safe.", statusCode: StatusCodes.Status504GatewayTimeout);
                    }
                });
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IMaxioClient maxio, CancellationToken cancellationToken) =>
            {
                try
                {
                    var user = await FindCurrentUserAsync(principal, users);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    var customer = await maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
                    if (customer is null)
                    {
                        return Results.Ok(new MySubscriptionsResponse(new List<SubscriptionDto>()));
                    }

                    var plans = (await maxio.ListPlansAsync(cancellationToken)).ToDictionary(x => x.Handle, StringComparer.Ordinal);
                    var subscriptions = (await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                        .Where(x => plans.TryGetValue(x.ProductHandle, out _))
                        .Select(x => SubscriptionDto.From(x, plans[x.ProductHandle]))
                        .ToList();
                    return Results.Ok(new MySubscriptionsResponse(subscriptions));
                }
                catch (MaxioApiException ex)
                {
                    return MaxioProblem(ex);
                }
                catch (HttpRequestException)
                {
                    return Results.Problem("Maxio could not be reached.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    private static async Task<ApplicationUser?> FindCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        await users.FindByNameAsync(principal.Identity?.Name ?? string.Empty);

    private static async Task StoreSubscriptionIdAsync(AppIdentityDbContext db, MaxioSubscriptionEnrollment enrollment, long subscriptionId, CancellationToken cancellationToken)
    {
        if (enrollment.MaxioSubscriptionId != subscriptionId)
        {
            enrollment.MaxioSubscriptionId = subscriptionId;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task ClearProvisioningLeaseAsync(AppIdentityDbContext db, MaxioSubscriptionEnrollment? enrollment, CancellationToken cancellationToken)
    {
        if (enrollment?.MaxioSubscriptionId is null && enrollment?.ProvisioningStartedAt is not null)
        {
            enrollment.ProvisioningStartedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IResult MaxioProblem(MaxioApiException exception) =>
        Results.Problem("Maxio rejected the subscription request.", statusCode: exception.StatusCode is >= 400 and < 500
            ? exception.StatusCode
            : StatusCodes.Status502BadGateway);

    private static string CustomerReference(string userId) => $"eshoponweb-user:{userId}";

    private static string SubscriptionReference(string userId, string productHandle) => $"eshoponweb-subscription:{userId}:{productHandle}";

    private static string CustomerFirstName(string email)
    {
        var separator = email.IndexOf('@');
        return separator > 0 ? email[..separator] : "Shopper";
    }
}

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit)
{
    public static SubscriptionPlanDto From(MaxioPlan plan) => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit);
}

public sealed record SubscriptionDto(long Id, string PlanHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(MaxioSubscription subscription, MaxioPlan plan) =>
        new(subscription.Id, plan.Handle, plan.Name, subscription.PriceInCents == 0 ? plan.PriceInCents : subscription.PriceInCents, subscription.State, subscription.NextBillingAt);
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans);

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription, bool Created);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
