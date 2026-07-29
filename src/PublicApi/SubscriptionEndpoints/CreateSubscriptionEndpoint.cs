using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, MaxioSubscriptionService service, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
                var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
                    return Results.Unauthorized();

                request.UserId = userId;
                request.UserEmail = userEmail;
                request.UserName = userName ?? userEmail;

                return await HandleAsync(request, service);
            })
           .Produces<SubscriptionResponseDto>()
           .Produces(400)
           .Produces(401)
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService service)
    {
        if (string.IsNullOrEmpty(request.PlanHandle))
            return Results.BadRequest("PlanHandle is required");

        var subscription = await service.CreateSubscriptionAsync(
            request.UserId!, request.UserEmail!, request.UserName!, request.PlanHandle);

        if (subscription == null)
            return Results.BadRequest("Failed to create subscription");

        return Results.Ok(new SubscriptionResponseDto
        {
            Id = subscription.Id,
            ProductName = subscription.ProductName,
            ProductHandle = subscription.ProductHandle,
            State = subscription.State,
            CreatedAt = subscription.CreatedAt,
            NextBillingAt = subscription.NextBillingAt
        });
    }
}

public class CreateSubscriptionRequest
{
    public string? PlanHandle { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
}

public class SubscriptionResponseDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; }
    public System.DateTime? NextBillingAt { get; set; }
}
