using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IMaxioService _maxioService;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public GetUserSubscriptionsEndpoint(
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IRepository<Subscription> subRepo) =>
            {
                return await HandleGetAsync(user, subRepo);
            })
           .RequireAuthorization()
           .Produces<GetUserSubscriptionsResponse>()
           .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
           .WithName("GetUserSubscriptions")
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioService service)
    {
        return Task.FromResult(Results.BadRequest(new ErrorResponse { Error = "Invalid request" }));
    }

    private async Task<IResult> HandleGetAsync(ClaimsPrincipal user, IRepository<Subscription> subRepo)
    {
        try
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return Results.Unauthorized();
            }

            var userId = userIdClaim.Value;
            var subscriptions = await subRepo.ListAsync();
            var userSubscriptions = subscriptions
                .Where(s => s.UserId == userId)
                .ToList();

            var response = new GetUserSubscriptionsResponse
            {
                Subscriptions = userSubscriptions
                    .Select(s => new SubscriptionDto
                    {
                        MaxioSubscriptionId = s.MaxioSubscriptionId,
                        MaxioCustomerId = s.MaxioCustomerId,
                        PlanHandle = s.PlanHandle,
                        Status = s.Status,
                        NextBillingDate = s.NextBillingDate,
                        CreatedAt = s.CreatedAt
                    })
                    .ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    public class GetUserSubscriptionsResponse
    {
        public List<SubscriptionDto> Subscriptions { get; set; } = new();
    }

    public class ErrorResponse
    {
        public string Error { get; set; } = null!;
    }
}
