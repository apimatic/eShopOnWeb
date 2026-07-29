using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated shopper. Returns an empty list when the user
/// has never subscribed (no backing Maxio customer yet). The subscriber identity comes from the JWT.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                if (!SubscriberIdentity.TryResolve(user, out var reference, out _, out _, out _))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(reference, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userReference, IMaxioBillingService billingService)
    {
        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(userReference);

            var response = new ListMySubscriptionsResponse
            {
                CustomerReference = userReference,
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
            };

            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return BillingResults.Problem(ex);
        }
    }
}
