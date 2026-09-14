using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the signed-in shopper.
/// </summary>
public class MySubscriptionsListEndpoint
    : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var email = BillingResults.GetSubscriberEmail(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            return BillingResults.MissingIdentity();
        }

        var response = new ListMySubscriptionsResponse();

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(
                new Subscriber(email), cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(subscription => subscription.ToDto()));
        }
        catch (BillingProviderException ex)
        {
            return BillingResults.Problem(ex);
        }

        return Results.Ok(response);
    }
}
