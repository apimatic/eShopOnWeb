using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional billing contact first name; defaults to one derived from the account.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing contact last name.</summary>
    public string? LastName { get; set; }

    /// <summary>The caller, taken from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when the user already held this plan and the existing subscription is returned.</summary>
    public bool Created { get; set; }
}

/// <summary>
/// Subscribe the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(request?.PlanHandle))
                {
                    return Results.BadRequest(new ErrorDetails { StatusCode = 400, Message = "planHandle is required." });
                }

                request.Subscriber = new SubscriberIdentity(userName, user.FindFirstValue(ClaimTypes.Email), request.FirstName, request.LastName);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.SubscribeAsync(request.Subscriber!, request.PlanHandle!.Trim());
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMapper.ToDto(result.Subscription),
            Created = result.Created,
        };

        // 201 for a new subscription, 200 when the idempotent call returned the one already held.
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
