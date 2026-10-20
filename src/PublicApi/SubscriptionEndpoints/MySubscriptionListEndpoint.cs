using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, BillingCaller?, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var userName = BillingEndpointSupport.UserNameOf(user);
                var caller = userName is null ? null : new BillingCaller(userName, Email: null);
                return await HandleAsync(caller, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithTags(BillingEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(BillingCaller? caller, SubscriptionBillingService billing) =>
        HandleAsync(caller, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(BillingCaller? caller, SubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();
        try
        {
            if (caller is null) throw BillingException.Unauthenticated();

            var subscriptions = await billing.GetMySubscriptionsAsync(caller, cancellationToken);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return BillingEndpointSupport.Problem(ex, response.CorrelationId());
        }
    }
}
