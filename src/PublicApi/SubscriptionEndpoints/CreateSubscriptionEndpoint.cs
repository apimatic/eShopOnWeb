using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, httpContext, maxioService);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        return Results.Ok(new { message = "OK" });
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Errors.Add("User not authenticated");
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                response.Errors.Add("ProductHandle is required");
                return Results.BadRequest(response);
            }

            var subscription = await maxioService.CreateSubscriptionAsync(userId, request.ProductHandle);

            response.Subscription = new SubscriptionResponse
            {
                Id = subscription.Id,
                CustomerId = subscription.CustomerId,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductPriceInCents = subscription.ProductPriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Errors.Add(ex.Message);
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public decimal ProductPrice => ProductPriceInCents / 100m;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionResponse? Subscription { get; set; }
    public List<string> Errors { get; } = new();
}
