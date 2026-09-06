using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioIntegration;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Gets all subscriptions for the authenticated user
/// </summary>
public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Get user subscriptions",
                Description = "Returns all subscriptions for the authenticated user",
                OperationId = "subscriptions.getUser",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);
                var response = new GetUserSubscriptionsResponse(Guid.NewGuid())
                {
                    Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                    {
                        SubscriptionId = s.SubscriptionId,
                        ProductHandle = s.ProductHandle,
                        ProductName = s.ProductName,
                        State = s.State,
                        PriceInCents = s.CurrentPriceInCents,
                        NextBillingAt = s.NextBillingAt,
                        ActivatedAt = s.ActivatedAt
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, ISubscriptionService subscriptionService)
    {
        throw new NotImplementedException("This endpoint is implemented as a minimal API");
    }
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
}
