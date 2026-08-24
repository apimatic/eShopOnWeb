using System;
using System.Linq;
using System.Net;
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
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a shopper with an existing
/// live subscription to the plan gets that subscription back instead of a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                MaxioApiClient maxio, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, userManager, maxio, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager, MaxioApiClient maxio, CancellationToken cancellationToken)
    {
        var appUser = await userManager.FindByNameAsync(user.Identity?.Name ?? string.Empty);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "ProductHandle is required." });
        }

        var plans = await maxio.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Results.BadRequest(new { message = $"Unknown plan handle '{request.ProductHandle}'." });
        }

        // The Identity user id is the stable, unique customer reference in Maxio.
        var customerReference = appUser.Id;
        var baseReference = $"{appUser.Id}:{plan.Handle}";

        var existing = await maxio.FindSubscriptionByReferenceAsync(baseReference, cancellationToken);
        if (existing is not null && SubscriptionMapper.IsLive(existing))
        {
            return Results.Ok(new SubscribeResponse(request.CorrelationId())
            {
                Subscription = SubscriptionMapper.ToDto(existing),
                IsNew = false
            });
        }

        var customer = await EnsureCustomerAsync(appUser, customerReference, maxio, cancellationToken);

        // A previous end-of-life subscription already holds the base reference; re-subscribes get a fresh one.
        var subscriptionReference = existing is null
            ? baseReference
            : $"{baseReference}:{Guid.NewGuid():N}";

        try
        {
            var created = await maxio.CreateSubscriptionAsync(plan.Handle!, customer.Reference!, subscriptionReference, cancellationToken);
            return Results.Created("api/my-subscriptions", new SubscribeResponse(request.CorrelationId())
            {
                Subscription = SubscriptionMapper.ToDto(created),
                IsNew = true
            });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have won the race; re-check before surfacing the error.
            var raced = await maxio.FindSubscriptionByReferenceAsync(baseReference, cancellationToken);
            if (raced is not null && SubscriptionMapper.IsLive(raced))
            {
                return Results.Ok(new SubscribeResponse(request.CorrelationId())
                {
                    Subscription = SubscriptionMapper.ToDto(raced),
                    IsNew = false
                });
            }
            throw;
        }
    }

    private static async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser appUser, string customerReference,
        MaxioApiClient maxio, CancellationToken cancellationToken)
    {
        var email = appUser.Email ?? appUser.UserName ?? customerReference;
        var localPart = email.Split('@')[0];
        var customer = await maxio.GetOrCreateCustomerAsync(
            customerReference,
            email,
            firstName: string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
            lastName: "Shopper",
            cancellationToken);

        if (string.IsNullOrEmpty(customer.Reference))
        {
            customer.Reference = customerReference;
        }
        return customer;
    }
}
