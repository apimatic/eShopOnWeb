using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListUserSubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var request = new ListUserSubscriptionsRequest { UserId = userId };
                return await HandleAsync(request, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetUserSubscriptions");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return Results.Unauthorized();
            }

            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(request.UserId);
            var subs = subscriptions.Select(s => new UserSubscriptionDto
            {
                SubscriptionId = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                NextBillingAt = s.NextBillingAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                CreatedAt = s.CreatedAt
            }).ToList();

            var response = new ListUserSubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subs
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListUserSubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse() { }
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
