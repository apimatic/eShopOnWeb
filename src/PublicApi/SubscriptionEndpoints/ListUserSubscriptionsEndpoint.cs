using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                var request = new ListUserSubscriptionsRequest(Guid.NewGuid(), userId);
                return await HandleAsync(request, subscriptionService);
            })
            .WithName("ListUserSubscriptions")
            .RequireAuthorization()
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request, MaxioSubscriptionService subscriptionService)
    {
        var response = new ListUserSubscriptionsResponse(request.CorrelationId());

        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var customer = await subscriptionService.GetCustomerByReferenceAsync(request.UserId);
            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(customer.Id);

            response.Subscriptions.AddRange(subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProductId = s.ProductId,
                State = s.State,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt
            }));

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListUserSubscriptionsRequest : BaseRequest
{
    public ListUserSubscriptionsRequest(Guid correlationId, string? userId)
    {
        _correlationId = correlationId;
        UserId = userId;
    }

    public string? UserId { get; set; }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
