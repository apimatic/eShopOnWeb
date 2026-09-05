using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated eShopOnWeb user to a Maxio Advanced Billing plan. This is
/// the "hero flow": ensures a Maxio customer exists for the caller (idempotent - a
/// double-click never creates two customers or two subscriptions), enrolls them in the
/// requested plan, and returns the resulting plan/price/state/next-billing-date.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService maxioService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var user = await CurrentUserAccessor.GetCurrentUserAsync(httpContext.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var profile = CurrentUserAccessor.ToCustomerProfile(user);
                request.UserId = profile.Reference;
                request.Email = profile.Email;
                request.FirstName = profile.FirstName;
                request.LastName = profile.LastName;

                return await HandleAsync(request, maxioService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var profile = new MaxioCustomerProfile(request.UserId, request.Email, request.FirstName, request.LastName);

        try
        {
            var subscription = await maxioService.SubscribeAsync(profile, request.PlanHandle);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = ToDto(subscription)
            };
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt
    };
}
