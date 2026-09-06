using System;
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
/// Creates a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Create a subscription",
                Description = "Subscribe the authenticated user to a plan",
                OperationId = "subscriptions.create",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var result = await subscriptionService.SubscribeUserAsync(userId, request.ProductHandle, request.ProductPricePointHandle);
                var response = new CreateSubscriptionResponse(request.CorrelationId())
                {
                    SubscriptionId = result.SubscriptionId,
                    ProductHandle = result.ProductHandle,
                    ProductName = result.ProductName,
                    State = result.State,
                    PriceInCents = result.CurrentPriceInCents,
                    NextBillingAt = result.NextBillingAt,
                    ActivatedAt = result.ActivatedAt
                };

                return Results.Created($"api/subscriptions/{result.SubscriptionId}", response);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        throw new NotImplementedException("This endpoint is implemented as a minimal API");
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string? ProductPricePointHandle { get; set; }
}
