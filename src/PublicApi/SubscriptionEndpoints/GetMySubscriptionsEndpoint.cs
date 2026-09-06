using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IRepository<Subscription> subscriptionRepository) =>
            {
                var response = new GetMySubscriptionsResponse();

                try
                {
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        response.Success = false;
                        response.ErrorMessage = "User not authenticated";
                        return Results.Unauthorized();
                    }

                    // Get user's subscriptions from database
                    var spec = new UserSubscriptionsSpecification(userId);
                    var subscriptions = await subscriptionRepository.ListAsync(spec);

                    if (subscriptions.Count == 0)
                    {
                        response.Success = true;
                        response.Subscriptions = new List<UserSubscriptionDto>();
                        return Results.Ok(response);
                    }

                    // Map to DTOs
                    response.Subscriptions = subscriptions.ConvertAll(s => new UserSubscriptionDto
                    {
                        Id = s.Id,
                        PlanHandle = s.PlanHandle,
                        State = s.State,
                        CurrentPrice = s.CurrentPrice,
                        NextBillingAt = s.NextBillingAt,
                        CreatedAt = s.CreatedAt
                    });

                    response.Success = true;
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return Results.BadRequest(response);
                }
            })
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
