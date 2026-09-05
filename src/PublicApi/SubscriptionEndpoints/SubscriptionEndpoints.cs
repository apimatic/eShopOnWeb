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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>JWT-protected subscription endpoints backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioClient maxio, CancellationToken cancellationToken) =>
                await ListPlansAsync(maxio, cancellationToken))
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, HttpContext context, UserManager<ApplicationUser> users, IMaxioClient maxio, ILogger<SubscriptionEndpoints> logger, CancellationToken cancellationToken) =>
                await SubscribeAsync(request, context.User, users, maxio, logger, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext context, UserManager<ApplicationUser> users, IMaxioClient maxio, CancellationToken cancellationToken) =>
                await ListMineAsync(context.User, users, maxio, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    // Required by the endpoint-discovery convention. The route registrations above use
    // the more specific handlers because they also need the authenticated caller.
    public Task<IResult> HandleAsync(IMaxioClient maxio) => ListPlansAsync(maxio, CancellationToken.None);

    private static async Task<IResult> ListPlansAsync(IMaxioClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var response = new SubscriptionPlanListResponse(Guid.NewGuid());
            response.Plans.AddRange((await maxio.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(ToPlan));
            return Results.Ok(response);
        }
        catch (Exception exception) { return MaxioFailure(exception); }
    }

    private static async Task<IResult> SubscribeAsync(SubscribeRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, IMaxioClient maxio, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = ["A plan handle is required."] });
        try
        {
            var user = await GetUserAsync(principal, users).ConfigureAwait(false);
            if (user is null) return Results.Unauthorized();
            var selectedPlan = (await maxio.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                .SingleOrDefault(product => product.ArchivedAt is null && string.Equals(product.Handle, request.PlanHandle, StringComparison.Ordinal));
            if (selectedPlan is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = ["The plan is not available."] });

            var customerReference = CustomerReference(user.Id);
            var customer = await EnsureCustomerAsync(maxio, customerReference, user, cancellationToken).ConfigureAwait(false);
            var subscriptionReference = SubscriptionReference(user.Id, selectedPlan.Handle!);
            var subscription = await maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
            var existing = subscription is not null;
            if (subscription is null)
            {
                try
                {
                    subscription = await maxio.CreateSubscriptionAsync(selectedPlan.Handle!, customerReference, subscriptionReference, cancellationToken).ConfigureAwait(false);
                }
                catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    // References are deterministic. A concurrent request may have won the create race;
                    // always re-read Maxio before reporting a failure.
                    subscription = await maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
                    if (subscription is null) throw;
                    existing = true;
                }
            }

            logger.LogInformation("Maxio subscription {SubscriptionId} returned for application user {UserId}.", subscription.Id, user.Id);
            var response = new SubscribeResponse(request.CorrelationId()) { Subscription = ToSubscription(subscription), ExistingSubscription = existing };
            return existing ? Results.Ok(response) : Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception exception) { return MaxioFailure(exception); }
    }

    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users, IMaxioClient maxio, CancellationToken cancellationToken)
    {
        try
        {
            var user = await GetUserAsync(principal, users).ConfigureAwait(false);
            if (user is null) return Results.Unauthorized();
            var customer = await maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken).ConfigureAwait(false);
            var response = new MySubscriptionsResponse(Guid.NewGuid());
            if (customer is not null)
                response.Subscriptions.AddRange((await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false)).Select(ToSubscription));
            return Results.Ok(response);
        }
        catch (Exception exception) { return MaxioFailure(exception); }
    }

    private static async Task<MaxioCustomer> EnsureCustomerAsync(IMaxioClient maxio, string reference, ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await maxio.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;
        var (firstName, lastName) = NameParts(user.UserName ?? user.Email ?? user.Id);
        try { return await maxio.CreateCustomerAsync(reference, firstName, lastName, user.Email ?? $"{user.Id}@eshop.invalid", cancellationToken).ConfigureAwait(false); }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var customer = await maxio.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (customer is null) throw;
            return customer;
        }
    }

    private static async Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        await users.FindByNameAsync(principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty).ConfigureAwait(false);

    private static (string FirstName, string LastName) NameParts(string value)
    {
        var parts = value.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => ("eShop", "Shopper"), 1 => (parts[0], "Shopper"), _ => (parts[0], parts[^1]) };
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!, Name = product.Name ?? product.Handle!, Description = product.Description,
        PriceInCents = product.PriceInCents, Interval = product.Interval, IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static MySubscriptionDto ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id, PlanHandle = subscription.Product.Handle ?? string.Empty, PlanName = subscription.Product.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents, State = subscription.State ?? string.Empty,
        NextBillingAt = subscription.CurrentPeriodEndsAt
    };

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{userId}-{planHandle}";

    private static IResult MaxioFailure(Exception exception) => exception switch
    {
        MaxioConfigurationException => Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable),
        MaxioApiException { StatusCode: HttpStatusCode.NotFound } => Results.Problem("The configured billing catalog was not found.", statusCode: StatusCodes.Status503ServiceUnavailable),
        MaxioApiException => Results.Problem("The billing service could not complete the request.", statusCode: StatusCodes.Status502BadGateway),
        HttpRequestException => Results.Problem("The billing service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Problem("The subscription request could not be completed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}
