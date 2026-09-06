using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext httpContext, CreateSubscriptionRequestPayload body, ISubscriptionService subscriptionService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(userEmail))
                {
                    return Results.BadRequest("User email not found in token");
                }

                return await HandleAsync(new CreateSubscriptionRequest(body.PlanHandle, userId, userEmail), subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest("Plan handle is required");
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(request.UserId, request.UserEmail, request.PlanHandle);

            var response = new CreateSubscriptionResponse()
            {
                Subscription = new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    ProductHandle = subscription.ProductHandle,
                    Balance = subscription.BalanceInCents / 100m,
                    NextBillingDate = subscription.NextBillingDate,
                    CreatedAt = subscription.CreatedAt
                }
            };

            return Results.Created($"/api/my-subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class CreateSubscriptionRequestPayload
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(string planHandle, string userId, string userEmail)
    {
        PlanHandle = planHandle;
        UserId = userId;
        UserEmail = userEmail;
    }

    public string PlanHandle { get; set; }
    public string UserId { get; set; }
    public string UserEmail { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}
