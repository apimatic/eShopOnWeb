using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a Maxio plan. Ensures a Maxio customer exists for the user
/// (idempotent - a double click never creates two customers) and enrolls them in the plan,
/// returning the existing subscription instead of creating a duplicate if the user already
/// has a non-terminal subscription to the same plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioService>
{
    /// <summary>
    /// Subscription states that do NOT block re-subscribing to the same plan: they represent
    /// a subscription that is over, not one that is merely having payment trouble.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, user, maxioService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioService maxioService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var reference = SubscriberIdentity.GetReference(user);
        if (string.IsNullOrEmpty(reference))
        {
            return Results.Unauthorized();
        }

        var email = SubscriberIdentity.GetEmail(user) ?? reference;
        var (firstName, lastName) = SplitName(email);

        var customer = await maxioService.EnsureCustomerAsync(reference, email, firstName, lastName);

        // Idempotency: if the user already has a non-terminal subscription to this plan,
        // return it instead of creating a duplicate (guards against double-clicks/retries).
        var existingSubscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);
        var existingMatch = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, request.ProductHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));

        if (existingMatch is not null)
        {
            response.Subscription = ListMySubscriptionsEndpoint.ToDto(existingMatch);
            response.AlreadySubscribed = true;
            return Results.Ok(response);
        }

        var created = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);
        response.Subscription = ListMySubscriptionsEndpoint.ToDto(created);
        response.AlreadySubscribed = false;

        return Results.Created($"api/my-subscriptions", response);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, 2, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Length > 1 ? Capitalize(parts[1]) : "Subscriber";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
