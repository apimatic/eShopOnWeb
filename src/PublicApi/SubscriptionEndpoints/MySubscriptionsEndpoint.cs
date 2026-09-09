using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
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
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new MySubscriptionsResponse();

        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");
        var appUser = await CurrentUser.ResolveAsync(context.User, context.RequestServices);
        if (appUser == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.ListUserSubscriptionsAsync(appUser.Id, context.RequestAborted);
            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                Reference = s.Reference,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                Price = s.Price,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
                Balance = s.Balance
            }));
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingResults.Problem(ex, response.CorrelationId());
        }
    }
}
