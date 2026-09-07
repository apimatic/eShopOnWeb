using System;
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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app, IServiceProvider services)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext context) =>
            {
                var maxioService = services.GetRequiredService<IMaxioService>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

                return await Handle(request, context, maxioService, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    private static async Task<IResult> Handle(
        CreateSubscriptionRequest request,
        HttpContext context,
        IMaxioService maxioService,
        UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse();

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

            // Get or create Maxio customer
            var maxioCustomer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                user.Email ?? "",
                user.UserName?.Split('@')[0] ?? "User",
                "");

            if (maxioCustomer == null)
            {
                return Results.BadRequest(new { error = "Failed to create/retrieve Maxio customer" });
            }

            // Create subscription in Maxio
            var maxioSubscription = await maxioService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.ProductHandle);

            response.Subscription = new UserSubscriptionDto
            {
                Id = 0,
                MaxioSubscriptionId = maxioSubscription.Id,
                Plan = new SubscriptionPlanDto
                {
                    Id = 0,
                    Handle = maxioSubscription.ProductHandle ?? "",
                    Name = maxioSubscription.ProductHandle ?? "",
                    Description = "",
                    PriceInCents = 0,
                    Interval = 1,
                    IntervalUnit = "month"
                },
                State = maxioSubscription.State,
                CurrentPeriodStartsAt = maxioSubscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = maxioSubscription.CurrentPeriodEndsAt,
                NextBillingAt = maxioSubscription.NextBillingAt
            };

            response.Message = "Subscription created successfully";
            return Results.Created($"api/subscriptions/{maxioSubscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public required string ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public UserSubscriptionDto? Subscription { get; set; }
    public string Message { get; set; } = "";
}
