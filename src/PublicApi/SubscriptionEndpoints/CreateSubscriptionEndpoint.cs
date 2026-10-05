using System;
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
/// Subscribes the authenticated shopper to a plan (by Maxio product handle). Idempotent: a repeat
/// call for a plan the shopper already holds returns the existing subscription; a double-click
/// never creates two subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionBody body, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                IMaxioBillingService billingService) =>
            {
                var shopper = await SubscriptionIdentity.ResolveAsync(user, userManager);
                if (shopper is null)
                    return Results.Unauthorized();

                return await HandleAsync(new CreateSubscriptionRequest(body?.ProductHandle) { Shopper = shopper }, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        try
        {
            var subscription = await billingService.SubscribeAsync(request.Shopper, request.ProductHandle ?? string.Empty);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDtos.FromSubscription(subscription)
            };
            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return MaxioBillingResults.Problem(ex);
        }
    }
}