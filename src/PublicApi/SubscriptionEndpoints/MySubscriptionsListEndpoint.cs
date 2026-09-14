using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the authenticated buyer's Maxio subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var enrollments = await subscriptionService.GetSubscriptionsForBuyerAsync(buyerId, cancellationToken);

        response.Subscriptions.AddRange(enrollments.Select(e => new SubscriptionDto
        {
            SubscriptionId = e.SubscriptionId,
            PlanHandle = e.PlanHandle,
            PlanName = e.PlanName,
            Price = e.Price,
            State = e.State,
            NextBillingDate = e.NextBillingDate,
            CreatedAt = e.CreatedAt,
            AlreadyExisted = e.AlreadyExisted
        }));

        return Results.Ok(response);
    }
}
