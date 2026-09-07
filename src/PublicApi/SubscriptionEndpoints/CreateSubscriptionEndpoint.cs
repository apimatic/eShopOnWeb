using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            response.Error = "PlanHandle is required";
            return Results.BadRequest(response);
        }

        try
        {
            var subscription = await _subscriptionService.CreateSubscriptionAsync(userId, request.PlanHandle);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                CustomerId = subscription.CustomerId,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.Error = $"Failed to create subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
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
    public string? Error { get; set; }
}
