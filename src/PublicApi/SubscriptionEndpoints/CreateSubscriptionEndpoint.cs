using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeated calls for the same
/// plan return the existing subscription instead of creating duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        var plans = await _billingService.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Results.NotFound(new { error = $"No plan with handle '{request.PlanHandle}' exists." });
        }

        var (firstName, lastName) = ResolveNames(username, request);

        var result = await _billingService.SubscribeAsync(
            customerReference: username,
            email: username,
            firstName: firstName,
            lastName: lastName,
            planHandle: plan.Handle!,
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static (string FirstName, string LastName) ResolveNames(string username, CreateSubscriptionRequest request)
    {
        var firstName = request.FirstName;
        var lastName = request.LastName;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            // eShopOnWeb accounts carry only an email/username; derive a presentable name from it.
            var localPart = username.Split('@')[0];
            var separators = new[] { '.', '-', '_' };
            var parts = localPart.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            firstName ??= parts.Length > 0 ? parts[0] : "Customer";
            lastName ??= parts.Length > 1 ? parts[^1] : "eShopOnWeb";
        }

        return (firstName!, lastName!);
    }
}
