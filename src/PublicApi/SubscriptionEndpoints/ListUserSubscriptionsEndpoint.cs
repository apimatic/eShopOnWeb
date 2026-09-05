using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsyncInternal)
           .Produces<ListUserSubscriptionsResponse>()
           .Produces(StatusCodes.Status401Unauthorized)
           .WithName("ListUserSubscriptions")
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> HandleAsyncInternal(HttpContext context, CatalogContext catalogContext)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await catalogContext.UserSubscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.SubscriptionPlan)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var dtos = subscriptions.Select(s => new UserSubscriptionDto
        {
            Id = s.Id,
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            PlanName = s.SubscriptionPlan.Name,
            State = s.State,
            BalanceInCents = s.BalanceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt
        }).ToList();

        var response = new ListUserSubscriptionsResponse { Subscriptions = dtos };
        return Results.Ok(response);
    }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
