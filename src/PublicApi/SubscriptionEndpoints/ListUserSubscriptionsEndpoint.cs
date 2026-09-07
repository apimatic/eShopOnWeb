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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app, IServiceProvider services)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext context) =>
            {
                var subscriptionRepository = services.GetRequiredService<IReadRepository<UserSubscription>>();
                var planRepository = services.GetRequiredService<IReadRepository<SubscriptionPlan>>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

                return await Handle(context, subscriptionRepository, planRepository, userManager);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    private static async Task<IResult> Handle(
        HttpContext context,
        IReadRepository<UserSubscription> subscriptionRepository,
        IReadRepository<SubscriptionPlan> planRepository,
        UserManager<ApplicationUser> userManager)
    {
        var response = new ListUserSubscriptionsResponse();

        try
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                        context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var userSubscriptionSpec = new UserSubscriptionsByUserIdSpec(userId);
            var userSubscriptions = await subscriptionRepository.ListAsync(userSubscriptionSpec);

            var subscriptionDtos = new List<UserSubscriptionDto>();

            foreach (var userSub in userSubscriptions)
            {
                var plan = userSub.SubscriptionPlan ?? await planRepository.GetByIdAsync(userSub.SubscriptionPlanId);
                if (plan == null)
                    continue;

                subscriptionDtos.Add(new UserSubscriptionDto
                {
                    Id = userSub.Id,
                    MaxioSubscriptionId = userSub.MaxioSubscriptionId,
                    Plan = new SubscriptionPlanDto
                    {
                        Id = plan.Id,
                        Handle = plan.Handle,
                        Name = plan.Name,
                        Description = plan.Description,
                        PriceInCents = plan.PriceInCents,
                        Interval = plan.Interval,
                        IntervalUnit = plan.IntervalUnit
                    },
                    State = userSub.State,
                    CurrentPeriodStartsAt = userSub.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = userSub.CurrentPeriodEndsAt
                });
            }

            response.Subscriptions = subscriptionDtos;
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
