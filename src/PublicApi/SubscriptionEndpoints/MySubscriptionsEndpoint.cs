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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions from Maxio.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext!.User.Identity!.Name
            : null;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        // UserManager is scoped (it owns the identity DbContext); endpoint classes are
        // resolved once at startup, so scoped services must be resolved per-request.
        var httpContext = _httpContextAccessor.HttpContext!;
        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.GetSubscriptionsForUserAsync(user.Id, CancellationToken.None);
        if (!result.IsSuccess)
        {
            return SubscriptionResultMapper.ToHttpResult(result, _ => Results.Ok());
        }

        var response = new MySubscriptionsResponse(Guid.NewGuid());
        response.Subscriptions.AddRange(result.Value.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            Currency = s.Currency,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            CurrentPeriodEnd = s.CurrentPeriodEnd,
            CreatedAt = s.CreatedAt,
            AlreadySubscribed = s.AlreadySubscribed
        }));

        return Results.Ok(response);
    }
}
