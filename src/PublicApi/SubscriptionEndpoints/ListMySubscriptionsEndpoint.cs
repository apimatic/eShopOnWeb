using System;
using System.Linq;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions with plan, price, state and next billing date.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billing)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        var userInfo = principal is null
            ? null
            : await SubscriptionUserResolver.ResolveAsync(principal, _userManager);
        if (userInfo is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.ListSubscriptionsForUserAsync(
                userInfo,
                httpContext?.RequestAborted ?? CancellationToken.None);

            var response = new ListMySubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));
            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToProblemResult();
        }
    }
}
