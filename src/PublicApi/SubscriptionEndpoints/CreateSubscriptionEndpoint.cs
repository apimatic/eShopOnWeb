using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// <para>
/// Idempotent by design: the billing service ensures exactly one provider customer exists for the
/// caller and returns the existing subscription instead of creating a second one, so a double-click
/// produces one customer and one subscription.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    /// <summary>
    /// Maxio handles are lowercase alphanumerics plus separators. Rejecting anything else here keeps
    /// junk out of the provider request instead of turning it into a 422 round-trip.
    /// </summary>
    private static readonly Regex PlanHandlePattern =
        new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (!SubscriberIdentityResolver.TryResolve(httpContext.User, out var subscriber))
        {
            return Results.Unauthorized();
        }

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle) || !PlanHandlePattern.IsMatch(planHandle))
        {
            return Results.BadRequest(new BlazorShared.Models.ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = $"'{nameof(request.PlanHandle)}' is required and must be a plan handle from GET /api/subscription-plans."
            });
        }

        var result = await billingService.SubscribeAsync(subscriber, planHandle, httpContext.RequestAborted);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadySubscribed = result.Outcome == SubscribeOutcome.AlreadySubscribed;

        // A repeat submit did not create anything, so it is not a 201.
        return response.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
