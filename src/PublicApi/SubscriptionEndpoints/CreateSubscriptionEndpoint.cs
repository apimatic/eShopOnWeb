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
/// Subscribes the logged-in shopper to a plan. Ensures a Maxio customer exists for the
/// eShopOnWeb user (idempotent on the user id) and enrolls them, unless they already have a
/// live subscription to that plan, in which case that subscription is returned instead of
/// creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired" };

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            throw new ArgumentException("PlanHandle is required.");

        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No authenticated user on the current request.");
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Token is missing the user identifier claim.");
        var email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? throw new InvalidOperationException("Token is missing the email claim.");

        var (firstName, lastName) = DeriveName(email);
        var customer = await billingService.GetOrCreateCustomerAsync(userId, email, firstName, lastName);

        // App-level idempotency guard: Maxio's own uniqueness_token only prevents literal
        // network-retry duplicates, so a double-click that reaches the server twice is
        // caught here instead, by treating any non-terminal existing subscription to the
        // same plan as "already subscribed".
        var existingSubscriptions = await billingService.GetSubscriptionsForCustomerAsync(customer.Id);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (existing is not null)
        {
            response.Subscription = SubscriptionMapper.ToDto(existing);
            response.AlreadyExisted = true;
            return Results.Ok(response);
        }

        var created = await billingService.CreateSubscriptionAsync(customer.Id, request.PlanHandle);
        response.Subscription = SubscriptionMapper.ToDto(created);
        return Results.Created("api/my-subscriptions", response);
    }

    /// <summary>
    /// ASP.NET Identity's ApplicationUser carries no first/last name, so we derive a
    /// reasonable display name from the email's local part for the Maxio customer record.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        return (localPart, "eShopOnWeb Customer");
    }
}
