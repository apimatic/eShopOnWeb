using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan: ensures a Maxio customer exists for
/// them (idempotent on their email) and enrolls them, or returns their existing enrollment if
/// they're already actively subscribed to that plan rather than creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly KeyedAsyncLock _enrollmentLock;

    public SubscribeEndpoint(KeyedAsyncLock enrollmentLock)
    {
        _enrollmentLock = enrollmentLock;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                request.BuyerEmail = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var plans = await maxioClient.ListPlansAsync();
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            var validHandles = string.Join(", ", plans.Select(p => p.Handle));
            return Results.BadRequest($"Unknown plan handle '{request.PlanHandle}'. Valid handles: {validHandles}");
        }

        var reference = BuildCustomerReference(request.BuyerEmail);

        // Ensuring the customer, checking for an existing enrollment, and creating the
        // subscription must run as one unit per shopper: Maxio dedupes customers by reference
        // server-side, but has no equivalent idempotency key for subscription creation, so two
        // near-simultaneous requests (a double-click) could otherwise both pass the
        // "not already subscribed" check and each create a subscription.
        using var _ = await _enrollmentLock.AcquireAsync(reference);

        var (firstName, lastName) = DeriveNameFromEmail(request.BuyerEmail);
        var customer = await maxioClient.EnsureCustomerAsync(reference, request.BuyerEmail, firstName, lastName);

        var existingSubscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.IsEnrolled && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (existing is not null)
        {
            response.AlreadySubscribed = true;
            response.Subscription = ToDto(existing);
            return Results.Ok(response);
        }

        var created = await maxioClient.CreateSubscriptionAsync(customer.Id, plan.Handle);
        response.AlreadySubscribed = false;
        response.Subscription = ToDto(created);
        return Results.Created("api/my-subscriptions", response);
    }

    internal static string BuildCustomerReference(string buyerEmail) =>
        $"eshoponweb:{buyerEmail.Trim().ToLowerInvariant()}";

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShopOnWeb";
        var lastName = segments.Length > 1 ? Capitalize(segments[1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
