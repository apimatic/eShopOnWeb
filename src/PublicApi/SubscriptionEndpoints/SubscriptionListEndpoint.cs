using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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
/// List subscriptions for the authenticated user
/// </summary>
public class SubscriptionListEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", Handle)
            .Produces<SubscriptionListResponse>()
            .RequireAuthorization()
            .WithName("ListUserSubscriptions")
            .WithTags("Subscriptions");
    }

    public async Task<IResult> Handle(HttpContext httpContext,
                                          UserManager<ApplicationUser> userManager, IMaxioService maxioService)
    {
        try
        {
            // Get the current user from the JWT token
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            // List subscriptions for this user
            var subscriptions = await maxioService.ListSubscriptionsAsync(user.Id);

            var response = new SubscriptionListResponse();
            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDetailDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                NextBillingAt = s.NextAssessmentAt,
                CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class SubscriptionDetailDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public class SubscriptionListResponse
{
    public List<SubscriptionDetailDto> Subscriptions { get; set; } = new();
}
