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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IReadRepository<Subscription> subscriptionRepository) =>
            {
                return await HandleAsync(user, subscriptionRepository);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IReadRepository<Subscription> subscriptionRepository)
    {
        var response = new GetUserSubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var spec = new UserSubscriptionsSpecification(userId);
            var subscriptions = await subscriptionRepository.ListAsync(spec);

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PlanPrice = s.PlanPrice,
                State = s.State.ToString(),
                CreatedDate = s.CreatedDate,
                NextBillingDate = s.NextBillingDate
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Failed to retrieve subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetUserSubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
    public string? ErrorMessage { get; set; }
}
