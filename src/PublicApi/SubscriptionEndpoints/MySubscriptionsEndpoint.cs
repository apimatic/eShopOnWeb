using System;
using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the signed-in user's own subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IMaxioSubscriptionService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), principal, service, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    // Required by IEndpoint; the route handler above supplies the caller identity and cancellation token.
    public Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioSubscriptionService service)
        => Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ClaimsPrincipal principal,
        IMaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        var user = string.IsNullOrEmpty(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user is null) return Results.Unauthorized();

        var subs = await service.GetMySubscriptionsAsync(user.Id, cancellationToken);
        var response = new MySubscriptionsResponse(request.CorrelationId());
        response.Subscriptions.AddRange(subs.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            State = s.State,
            NextBillingDate = s.NextBillingDate
        }));
        return Results.Ok(response);
    }
}
