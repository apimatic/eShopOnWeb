using System;
using System.Collections.Generic;
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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext httpContext, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), subscriptionService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, SubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, SubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? $"{username}@eshop.local";

        if (string.IsNullOrEmpty(username))
        {
            response.Error = "User identity not found";
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(username, email);
            if (subscriptions != null)
            {
                response.Subscriptions.AddRange(subscriptions);
                return Results.Ok(response);
            }

            response.Error = "Failed to retrieve subscriptions";
            return Results.BadRequest(response);
        }
        catch (Exception ex)
        {
            response.Error = $"Error retrieving subscriptions: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class ListMySubscriptionsRequest : BaseRequest
{
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    [JsonPropertyName("subscriptions")]
    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
