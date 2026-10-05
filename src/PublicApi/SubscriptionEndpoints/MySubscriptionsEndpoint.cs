using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as recorded in the billing system.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                var shopper = await SubscriptionIdentity.ResolveAsync(user, userManager);
                if (shopper is null)
                    return Results.Unauthorized();

                return await HandleAsync(new MySubscriptionsRequest { Shopper = shopper }, billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
    {
        try
        {
            var subscriptions = await billingService.GetSubscriptionsForShopperAsync(request.Shopper);

            var response = new MySubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtos.FromSubscription));

            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return MaxioBillingResults.Problem(ex);
        }
    }
}