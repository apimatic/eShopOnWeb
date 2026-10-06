using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions held by the billing system
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userName = user?.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        ListMySubscriptionsResponse response;
        try
        {
            var summaries = await subscriptionService.GetSubscriptionsForUserAsync(
                SubscriberProfiles.FromUser(applicationUser),
                httpContext?.RequestAborted ?? default);
            response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(summaries.Select(s => new SubscriptionSummaryDto
            {
                SubscriptionId = s.SubscriptionId,
                Reference = s.Reference,
                State = s.State,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                NextBillingDate = s.NextBillingDate,
                NextAssessmentAt = s.NextAssessmentAt,
                CustomerId = s.CustomerId,
                CustomerReference = s.CustomerReference
            }));
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingProblem.From(ex);
        }

        return Results.Ok(response);
    }
}