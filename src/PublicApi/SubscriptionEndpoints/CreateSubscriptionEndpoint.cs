using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Ensures a Maxio customer exists for the user
/// (keyed by their email, matching the identity token every other per-user Maxio call uses)
/// and, if the user is already subscribed to the requested plan, returns that subscription
/// instead of creating a duplicate - so a double-click / retried request is a no-op rather
/// than a second subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    // Subscriptions in these states are dead; a request for a plan the customer is in one
    // of these states for should create a fresh subscription rather than being treated as
    // "already subscribed".
    private static readonly string[] TerminalStates = { "canceled", "expired", "failed_to_create" };

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                request.CustomerEmail = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var customer = await GetOrCreateCustomerAsync(maxioClient, request.CustomerEmail);

        var existingSubscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State, StringComparer.OrdinalIgnoreCase));

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (existing is not null)
        {
            response.Subscription = Map(existing);
            response.AlreadySubscribed = true;
            return Results.Ok(response);
        }

        var created = await maxioClient.CreateSubscriptionAsync(customer.Id, request.PlanHandle);
        response.Subscription = Map(created);
        return Results.Created("api/my-subscriptions", response);
    }

    private static async Task<MaxioCustomer> GetOrCreateCustomerAsync(IMaxioClient maxioClient, string email)
    {
        var existing = await maxioClient.FindCustomerByReferenceAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var localPart = email.Split('@')[0];
            return await maxioClient.CreateCustomerAsync(reference: email, email: email, firstName: localPart, lastName: "eShopOnWeb Customer");
        }
        catch (MaxioApiException)
        {
            // Reference is unique in Maxio, so a failure here most likely means a concurrent
            // request (e.g. a double-click) already created the customer. Re-fetch rather
            // than failing the request.
            var afterRace = await maxioClient.FindCustomerByReferenceAsync(email);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    private static SubscriptionDto Map(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
