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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var firstName = httpContext.User.FindFirst("given_name")?.Value ?? "Customer";
                var lastName = httpContext.User.FindFirst("family_name")?.Value ?? "";

                if (string.IsNullOrEmpty(request.PlanHandle))
                {
                    return Results.BadRequest("Plan handle is required");
                }

                var subscription = await subscriptionService.SubscribeToPlanAsync(
                    userId, email, firstName, lastName, request.PlanHandle);

                var response = new CreateSubscriptionResponse
                {
                    CorrelationId = request.CorrelationId(),
                    Subscription = new SubscriptionDetailsDto
                    {
                        Id = subscription.Id,
                        CustomerId = subscription.CustomerId,
                        PlanHandle = subscription.PlanHandle,
                        PlanName = subscription.PlanName,
                        Status = subscription.Status,
                        PriceInCents = subscription.PriceInCents,
                        PriceFormatted = subscription.PriceFormatted,
                        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                        NextBillingAt = subscription.NextBillingAt
                    }
                };

                return Results.Created($"api/subscriptions/{subscription.Id}", response);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = null!;

    public string CorrelationId() => Guid.NewGuid().ToString();
}

public class CreateSubscriptionResponse
{
    public string CorrelationId { get; set; } = null!;
    public SubscriptionDetailsDto Subscription { get; set; } = null!;
}

public class SubscriptionDetailsDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = null!;
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime NextBillingAt { get; set; }
}
