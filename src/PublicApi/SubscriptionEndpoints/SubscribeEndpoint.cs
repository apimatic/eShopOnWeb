using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the calling (JWT-authenticated) shopper in a subscription plan. Ensures a Maxio
/// customer exists for the shopper and creates the subscription - both idempotently, so a
/// double-click never creates duplicate customers or subscriptions (see
/// <see cref="Microsoft.eShopWeb.Infrastructure.Services.MaxioSubscriptionService"/>).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequestBody body, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
             IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.PlanHandle))
                {
                    return Results.BadRequest("planHandle is required.");
                }

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

                var email = appUser.Email ?? username;
                var localPart = email.Split('@')[0];

                var request = new SubscribeRequest(appUser.Id, email, localPart, "Customer", body.PlanHandle);
                return await HandleAsync(request, subscriptionService, ct);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService, CancellationToken ct = default)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var customer = new SubscribingCustomer
        {
            UserId = request.UserId,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var subscription = await subscriptionService.SubscribeAsync(customer, request.PlanHandle, ct);

        response.Subscription = new CustomerSubscriptionDto
        {
            SubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
            State = subscription.State,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };

        return Results.Ok(response);
    }
}
