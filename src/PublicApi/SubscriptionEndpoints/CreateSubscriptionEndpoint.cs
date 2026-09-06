using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionBody body, MaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                var request = new CreateSubscriptionApiRequest(Guid.NewGuid(), body, userId);
                return await HandleAsync(request, subscriptionService);
            })
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, MaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            if (string.IsNullOrWhiteSpace(request.Body?.ProductHandle))
            {
                return Results.BadRequest(new { error = "ProductHandle is required" });
            }

            var userId = request.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var customer = await subscriptionService.EnsureCustomerAsync(
                userId: userId,
                email: request.Email ?? $"{userId}@eshop.local",
                firstName: request.FirstName ?? "eShop",
                lastName: request.LastName ?? "Customer");

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                customerId: customer.Id,
                productHandle: request.Body.ProductHandle);

            response.Subscription = new SubscriptionResponseDto
            {
                Id = subscription.Id,
                CustomerId = subscription.CustomerId,
                ProductId = subscription.ProductId,
                State = subscription.State,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionApiRequest : BaseRequest
{
    public CreateSubscriptionApiRequest(Guid correlationId, CreateSubscriptionBody body, string? userId = null)
    {
        _correlationId = correlationId;
        Body = body;
        UserId = userId;
    }

    public CreateSubscriptionBody? Body { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class CreateSubscriptionBody
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionResponseDto? Subscription { get; set; }
}

public class SubscriptionResponseDto
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
