using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal user,
                IMaxioService maxioService,
                IRepository<Subscription> subscriptionRepository,
                UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, user, maxioService, subscriptionRepository, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { message = "Plan handle is required" });
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var appUser = await userManager.FindByIdAsync(userId);
            if (appUser == null)
            {
                return Results.NotFound(new { message = "User not found" });
            }

            var plan = await maxioService.GetPlanAsync(request.PlanHandle);
            var maxioCustomer = await maxioService.GetOrCreateCustomerAsync(appUser.Email ?? userId, userId);

            if (maxioCustomer == null)
            {
                return Results.BadRequest(new { message = "Failed to create or retrieve customer in Maxio" });
            }

            var maxioSubscription = await maxioService.CreateSubscriptionAsync(maxioCustomer.Id, request.PlanHandle);

            var subscription = new Subscription(
                userId,
                maxioSubscription.Id,
                plan.Handle,
                plan.Price,
                plan.Name,
                MapStateToSubscriptionState(maxioSubscription.State),
                maxioSubscription.NextBillingAt ?? DateTime.UtcNow.AddMonths(1)
            );

            var createdSubscription = await subscriptionRepository.AddAsync(subscription);

            response.Subscription = new SubscriptionDto
            {
                Id = createdSubscription.Id,
                MaxioSubscriptionId = createdSubscription.MaxioSubscriptionId,
                PlanHandle = createdSubscription.PlanHandle,
                PlanName = createdSubscription.PlanName,
                PlanPrice = createdSubscription.PlanPrice,
                State = createdSubscription.State.ToString(),
                CreatedDate = createdSubscription.CreatedDate,
                NextBillingDate = createdSubscription.NextBillingDate
            };

            return Results.Created($"api/subscriptions/{createdSubscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Failed to create subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }

    private static SubscriptionState MapStateToSubscriptionState(string maxioState)
    {
        return maxioState switch
        {
            "active" => SubscriptionState.Active,
            "paused" => SubscriptionState.Paused,
            "pending" => SubscriptionState.Pending,
            "canceled" => SubscriptionState.Canceled,
            "expired" => SubscriptionState.Expired,
            "trialing" => SubscriptionState.Trialing,
            "assigning" => SubscriptionState.Assigning,
            "awaiting_signup" => SubscriptionState.AwaitingSignup,
            _ => SubscriptionState.Pending
        };
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}
