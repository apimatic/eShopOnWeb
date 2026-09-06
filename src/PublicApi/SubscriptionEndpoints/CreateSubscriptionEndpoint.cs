using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateApiSubscriptionRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateApiSubscriptionRequest request, HttpContext httpContext, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateApiSubscriptionRequest request, SubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(CreateApiSubscriptionRequest request, SubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? $"{username}@eshop.local";

        if (string.IsNullOrEmpty(username))
        {
            response.Error = "User identity not found";
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            response.Error = "ProductHandle is required";
            return Results.BadRequest(response);
        }

        var (subscription, error) = await subscriptionService.CreateSubscriptionAsync(username, email, request.ProductHandle);

        if (!string.IsNullOrEmpty(error))
        {
            response.Error = error;
            return Results.BadRequest(response);
        }

        if (subscription == null)
        {
            response.Error = "Failed to create subscription";
            return Results.BadRequest(response);
        }

        response.Subscription = subscription;
        response.Success = true;

        return Results.Created($"/api/subscriptions/{subscription.Id}", response);
    }
}

public class CreateApiSubscriptionRequest : BaseRequest
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
