using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException("This method is not used; use the other overload.");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService)
    {
        var userEmail = httpContext.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var userName = httpContext.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? "User";

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.CreateSubscriptionAsync(userEmail, userName, request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionResponse
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                ProductPrice = subscription.ProductPrice,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
            }
        };

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionResponse Subscription { get; set; } = new();
}
