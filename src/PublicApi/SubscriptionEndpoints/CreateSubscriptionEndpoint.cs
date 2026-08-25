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
/// Subscribes the authenticated shopper to a plan. Idempotent: repeating the call for a plan
/// the shopper is already subscribed to returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(
            request.Username,
            email: request.Username,
            request.FirstName,
            request.LastName,
            request.PlanHandle);

        response.Subscription = ToDto(subscription);
        return Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Models.SubscriptionDetails s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        ActivatedAt = s.ActivatedAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt
    };
}
