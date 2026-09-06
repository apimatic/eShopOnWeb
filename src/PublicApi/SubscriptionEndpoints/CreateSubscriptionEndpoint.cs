using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, MaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var endpoint = new CreateSubscriptionEndpoint();
                return await endpoint.HandleAsyncInternal(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the private overload with HttpContext");
    }

    private async Task<IResult> HandleAsyncInternal(CreateSubscriptionRequest request, MaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var firstName = httpContext.User.FindFirst("first_name")?.Value ?? "User";
            var lastName = httpContext.User.FindFirst("last_name")?.Value ?? "Account";
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "user@example.com";

            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest(new { error = "User identity not found in token" });
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "Plan handle is required" });
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                userId,
                firstName,
                lastName,
                email,
                request.PlanHandle);

            response.Subscription = new SubscriptionDetailDto
            {
                Id = subscription.Id,
                State = subscription.State,
                CustomerId = subscription.CustomerId,
                ProductId = subscription.ProductId,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (MaxioServiceException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDetailDto Subscription { get; set; } = new();
}

public class SubscriptionDetailDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long CurrentBillingAmountInCents { get; set; }
}
