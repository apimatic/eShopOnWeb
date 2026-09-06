using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IMaxioService _maxioService;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioService service, IRepository<Subscription> subRepo, UserManager<ApplicationUser> userMgr) =>
            {
                return await HandlePostAsync(request, user, service, subRepo, userMgr);
            })
           .RequireAuthorization()
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
           .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
           .WithName("CreateSubscription")
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioService service)
    {
        return Task.FromResult(Results.BadRequest(new ErrorResponse { Error = "Invalid request" }));
    }

    private async Task<IResult> HandlePostAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        IMaxioService service,
        IRepository<Subscription> subRepo,
        UserManager<ApplicationUser> userMgr)
    {
        try
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return Results.Unauthorized();
            }

            var userId = userIdClaim.Value;
            var appUser = await userMgr.FindByIdAsync(userId);
            if (appUser == null)
            {
                return Results.BadRequest(new ErrorResponse { Error = "User not found" });
            }

            var maxioCustomer = await service.GetOrCreateCustomerAsync(
                userId,
                appUser.Email ?? "",
                appUser.UserName ?? "",
                "");

            var maxioSubscription = await service.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.PlanHandle);

            var subscription = new Subscription(
                userId,
                maxioCustomer.Id,
                maxioSubscription.Id,
                request.PlanHandle,
                maxioSubscription.State ?? "unknown",
                maxioSubscription.NextBillingAt);

            await subRepo.AddAsync(subscription);

            var response = new CreateSubscriptionResponse
            {
                MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                MaxioCustomerId = subscription.MaxioCustomerId,
                PlanHandle = subscription.PlanHandle,
                Status = subscription.Status,
                NextBillingDate = subscription.NextBillingDate,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"/api/subscriptions/{subscription.MaxioSubscriptionId}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    public class CreateSubscriptionRequest
    {
        public string PlanHandle { get; set; } = null!;
    }

    public class CreateSubscriptionResponse
    {
        public long MaxioSubscriptionId { get; set; }
        public int MaxioCustomerId { get; set; }
        public string PlanHandle { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? NextBillingDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ErrorResponse
    {
        public string Error { get; set; } = null!;
    }
}
