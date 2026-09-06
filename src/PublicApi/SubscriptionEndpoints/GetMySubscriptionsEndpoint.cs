using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}

public class ListMySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class GetMySubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioService maxioService, UserManager<ApplicationUser> userManager, IReadRepository<Subscription> subscriptionRepository, HttpContext context) =>
            {
                var userIdClaim = context.User?.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(userIdClaim);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var userSubscriptions = (await subscriptionRepository.ListAsync())
                        .Where(s => s.UserId == user.Id)
                        .ToList();

                    var response = new ListMySubscriptionsResponse();

                    foreach (var sub in userSubscriptions)
                    {
                        var maxioSubs = await maxioService.GetCustomerSubscriptionsAsync(sub.MaxioCustomerId);
                        var matchingSub = maxioSubs.FirstOrDefault(m => m.Id == sub.MaxioSubscriptionId);

                        if (matchingSub != null)
                        {
                            response.Subscriptions.Add(new MySubscriptionDto
                            {
                                Id = matchingSub.Id,
                                State = matchingSub.State,
                                PlanHandle = matchingSub.PlanHandle,
                                PlanName = matchingSub.PlanName,
                                NextBillingDate = matchingSub.NextBillingDate
                            });
                        }
                    }

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest($"Error fetching subscriptions: {ex.Message}");
                }
            })
           .WithName("GetMySubscriptions")
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithOpenApi()
           .WithSummary("Get current user subscriptions")
           .WithDescription("Returns the subscription list for the authenticated user");
    }
}
