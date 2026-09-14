using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), userManager, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager, IMaxioBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext is available for the subscription request.");
        var (subscriber, problem) = await BillingIdentity.ResolveAsync(httpContext, userManager);
        if (subscriber is null)
        {
            return problem!;
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsForUserAsync(subscriber);
            var response = new ListMySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions.Select(CreateSubscriptionEndpoint.ToDto).ToList(),
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingHttpResults.From(ex);
        }
    }
}
