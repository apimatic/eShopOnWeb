using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext context, MaxioSubscriptionService service) =>
            {
                try
                {
                    var userId = context.User.FindFirst("sub")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
                    var userFullName = context.User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
                    var nameParts = userFullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    var firstName = nameParts.Length > 0 ? nameParts[0] : "Unknown";
                    var lastName = nameParts.Length > 1 ? nameParts[1] : "User";

                    if (string.IsNullOrEmpty(userEmail))
                    {
                        userEmail = $"{userId}@eshop.local";
                    }

                    var subscription = await service.CreateSubscriptionAsync(
                        userId,
                        userEmail,
                        firstName,
                        lastName,
                        request.ProductId);

                    var response = new CreateSubscriptionResponse
                    {
                        Id = subscription.Id,
                        CustomerId = subscription.CustomerId,
                        ProductId = subscription.ProductId,
                        State = subscription.State,
                        NextBillingDate = subscription.NextBillingDate,
                        ActivatedAt = subscription.ActivatedAt
                    };

                    return Results.Ok(response);
                }
                catch (MaxioException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            })
           .Produces<CreateSubscriptionResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    public class CreateSubscriptionRequest
    {
        public int ProductId { get; set; }
    }

    public class CreateSubscriptionResponse
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
        public long ProductId { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTimeOffset? NextBillingDate { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
    }
}
