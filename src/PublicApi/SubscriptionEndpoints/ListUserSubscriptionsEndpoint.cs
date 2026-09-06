using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService) =>
            {
                var userId = userManager.GetUserId(context.User);
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                    return Results.NotFound("User not found");

                if (!user.MaxioCustomerId.HasValue)
                {
                    return Results.Ok(new ListUserSubscriptionsResponse
                    {
                        Subscriptions = new List<UserSubscriptionResponse>()
                    });
                }

                try
                {
                    var subscriptions = await subscriptionService.ListCustomerSubscriptionsAsync(user.MaxioCustomerId.Value);

                    var response = new ListUserSubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(s => new UserSubscriptionResponse
                        {
                            SubscriptionId = s.Id,
                            State = s.State,
                            ProductName = s.ProductName,
                            ProductHandle = s.ProductHandle,
                            PriceInCents = s.ProductPriceInCents,
                            NextBillingDate = s.NextAssessmentAt,
                            CreatedAt = s.CreatedAt
                        }).ToList()
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class UserSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
