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
/// Subscribes the current user to a Maxio subscription plan (the hero flow): ensures a Maxio
/// customer exists for them, then enrolls them in the requested plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionBody body, ClaimsPrincipal user, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                var username = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(username))
                {
                    return Results.Unauthorized();
                }

                // The JWT carries only a username (the account's email) and no separate name
                // claims, so the customer's first/last name are derived from it server-side
                // rather than accepted as client input.
                var atIndex = username.IndexOf('@');
                var localPart = atIndex > 0 ? username[..atIndex] : username;

                var request = new CreateSubscriptionRequest
                {
                    PlanHandle = body.PlanHandle,
                    CustomerReference = username,
                    CustomerEmail = username,
                    CustomerFirstName = localPart,
                    CustomerLastName = "eShopOnWeb Customer"
                };

                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await maxioSubscriptionService.SubscribeAsync(
            request.CustomerReference,
            request.CustomerEmail,
            request.CustomerFirstName,
            request.CustomerLastName,
            request.PlanHandle,
            default);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt
        };

        return Results.Created("api/my-subscriptions", response);
    }
}
