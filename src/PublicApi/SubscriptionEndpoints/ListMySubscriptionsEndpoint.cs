using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the caller's subscriptions as recorded in the billing system of record.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioBillingService _billingService;

    public ListMySubscriptionsEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), user, userManager, httpContext.RequestAborted);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, UserManager<ApplicationUser> userManager) =>
        HandleAsync(request, user: null, userManager, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ClaimsPrincipal? user, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var appUser = await SubscriptionUsers.ResolveCurrentUserAsync(user, userManager);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var customerReference = SubscriptionUsers.CustomerReferenceFor(appUser.Id);
            var subscriptions = await _billingService.ListMySubscriptionsAsync(customerReference, cancellationToken);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.SubscriptionId,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                Price = s.Price,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                CustomerReference = s.CustomerReference,
                AlreadySubscribed = s.AlreadySubscribed
            }));
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return BillingErrorResults.From(ex, request.CorrelationId());
        }
    }
}