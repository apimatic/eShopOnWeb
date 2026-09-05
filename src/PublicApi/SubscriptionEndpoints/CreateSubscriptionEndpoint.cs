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
/// Enrolls the authenticated user in a Maxio subscription plan. Idempotent: ensures a Maxio
/// customer exists for the user and re-uses an existing live subscription to the same plan
/// instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        var customerReference = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(customerReference) || string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var plans = await subscriptionService.GetAvailablePlansAsync();
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, System.StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Results.NotFound(new { message = $"Unknown subscription plan handle '{request.PlanHandle}'." });
        }

        // ApplicationUser carries no first/last name - derive a display name from the email local part.
        var localPart = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        const string lastName = "Customer";

        var enrollment = new SubscriptionEnrollmentRequest(customerReference, email, firstName, lastName, plan.Handle, plan.Interval, plan.IntervalUnit);
        var subscription = await subscriptionService.SubscribeAsync(enrollment);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.PriceAmount,
                Interval = subscription.Interval,
                IntervalUnit = subscription.IntervalUnit,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate
            }
        };

        return Results.Ok(response);
    }
}
