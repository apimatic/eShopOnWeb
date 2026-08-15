using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions. Returns an empty list when the user has never
/// subscribed (no Maxio customer yet).
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext httpContext, CancellationToken ct) =>
            {
                var request = new GetMySubscriptionsRequest
                {
                    Subscriber = SubscriberIdentityFactory.FromPrincipal(httpContext.User),
                    CancellationToken = ct
                };
                return await HandleAsync(request, billingService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.GetMySubscriptionsAsync(request.Subscriber, request.CancellationToken);
            response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return BillingProblemResults.ToResult(ex);
        }
    }
}
