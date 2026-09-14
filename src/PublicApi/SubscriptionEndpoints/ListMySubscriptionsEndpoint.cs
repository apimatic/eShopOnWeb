using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling (JWT-authenticated) shopper's subscriptions, read live from Maxio - the
/// system of record - so plan/price/state/next-billing-date are always current.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
             IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                var username = principal.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Results.Unauthorized();
                }

                var appUser = await userManager.FindByNameAsync(username);
                if (appUser is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest(appUser.Id), subscriptionService, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService, CancellationToken ct = default)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.ListSubscriptionsForUserAsync(request.UserId, ct);

        response.Subscriptions.AddRange(subscriptions.Select(s => new CustomerSubscriptionDto
        {
            SubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.PriceInCents.HasValue ? s.PriceInCents.Value / 100m : null,
            State = s.State,
            NextAssessmentAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
        }));

        return Results.Ok(response);
    }
}
