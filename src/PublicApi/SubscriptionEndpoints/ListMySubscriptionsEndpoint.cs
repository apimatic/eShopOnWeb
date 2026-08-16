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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions. The shopper is identified by the JWT.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await HandleAsync(user, billingService, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return Results.BadRequest(new { errors = ex.Errors });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "The billing provider could not be reached.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
