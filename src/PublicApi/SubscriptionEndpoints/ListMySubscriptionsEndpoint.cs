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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(user, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's subscriptions", "Lists the subscriptions belonging to the authenticated user."));
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var subscriber = user.ToSubscriberIdentity();
        if (subscriber is null)
        {
            return SubscriptionErrorResults.MissingIdentity();
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
            };
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionErrorResults.FromBillingException(ex);
        }
    }
}
