using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NoContent());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CatalogContext catalogContext,
                   UserManager<ApplicationUser> userManager,
                   HttpContext httpContext) =>
            {
                try
                {
                    var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                    if (string.IsNullOrEmpty(userName))
                    {
                        return Results.BadRequest(new { success = false, message = "Unable to determine current user" });
                    }

                    var user = await userManager.FindByNameAsync(userName);
                    if (user == null)
                    {
                        return Results.BadRequest(new { success = false, message = "User not found" });
                    }

                    var subscriptions = await catalogContext.Subscriptions
                        .Where(s => s.UserId == user.Id)
                        .Include(s => s.SubscriptionPlan)
                        .OrderByDescending(s => s.CreatedAt)
                        .ToListAsync();

                    var response = new GetMySubscriptionsResponse(Guid.NewGuid())
                    {
                        Success = true
                    };

                    foreach (var sub in subscriptions)
                    {
                        response.Subscriptions.Add(new SubscriptionDto
                        {
                            Id = sub.Id,
                            SubscriptionPlanId = sub.SubscriptionPlanId,
                            PlanName = sub.SubscriptionPlan?.Name ?? "Unknown",
                            Status = sub.Status,
                            CreatedAt = sub.CreatedAt,
                            CanceledAt = sub.CanceledAt,
                            NextBillingDate = sub.NextBillingDate,
                            CurrentPrice = sub.CurrentPrice
                        });
                    }

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { success = false, message = $"Failed to fetch subscriptions: {ex.Message}" });
                }
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<SubscriptionDto> Subscriptions { get; set; }
}
