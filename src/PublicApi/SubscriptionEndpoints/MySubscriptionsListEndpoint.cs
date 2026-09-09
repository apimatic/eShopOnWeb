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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the authenticated shopper. The identity comes from the
/// JWT; returns an empty list if the user has never subscribed.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
                await HandleAsync(SubscriptionEndpointHelpers.GetUserName(user) ?? string.Empty, billing, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(string userName, ISubscriptionBillingService billing)
        => HandleAsync(userName, billing, CancellationToken.None);

    private async Task<IResult> HandleAsync(string userName, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.GetSubscriptionsForUserAsync(userName, cancellationToken);

            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionEndpointHelpers.ToDto).ToList(),
            };

            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
