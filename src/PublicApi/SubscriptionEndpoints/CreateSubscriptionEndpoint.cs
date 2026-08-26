using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the call for a plan the
/// shopper is already enrolled in returns the existing subscription (AlreadyExisted = true).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new CreateSubscriptionResponse(request.CorrelationId())
            {
                ErrorMessage = "productHandle is required. Call GET /api/subscription-plans to discover available plans."
            });
        }

        var (firstName, lastName) = DeriveName(username);

        var result = await billingService.SubscribeAsync(new SubscribeRequest
        {
            CustomerReference = username,
            Email = username,
            FirstName = request.FirstName ?? firstName,
            LastName = request.LastName ?? lastName,
            PlanHandle = request.ProductHandle
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ListSubscriptionPlansEndpoint.MapSubscription(result.Subscription),
            AlreadyExisted = result.AlreadyExisted
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// eShopOnWeb identities carry only an email address; Maxio customers require a first and last
    /// name, so they are derived from the email local part unless the caller supplies them.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "Customer";
        var lastName = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        string.Concat(value.Substring(0, 1).ToUpperInvariant(), value.AsSpan(1));
}
