using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioApiClient maxioClient) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new GetMySubscriptionsRequest(), maxioClient, userId);
            })
            .Produces<GetMySubscriptionsResponse>()
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioApiClient maxioClient, string userId)
    {
        try
        {
            var response = new GetMySubscriptionsResponse(request.CorrelationId());
            var subscriptions = await maxioClient.GetCustomerSubscriptions(userId);

            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionDetailsResponse
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductHandle = subscription.ProductHandle,
                    ProductName = subscription.ProductName,
                    CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    UpdatedAt = subscription.UpdatedAt
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = new List<SubscriptionDetailsResponse>();
    }

    public List<SubscriptionDetailsResponse> Subscriptions { get; set; }
}

public class SubscriptionDetailsResponse
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
