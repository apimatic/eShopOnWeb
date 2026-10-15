using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the caller already held this subscription (a repeated or double-clicked request).</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan. Repeating the request never creates a second customer or subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                var userName = BillingResults.UserName(user);
                if (string.IsNullOrWhiteSpace(userName))
                    return Results.Unauthorized();
                return await HandleAsync(request, billing, userName, ct);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing) =>
        throw new NotSupportedException("The caller identity is required; use the overload taking a user name.");

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing, string userName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        try
        {
            var result = await billing.SubscribeAsync(userName, request.PlanHandle, ct);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDto.From(result.Subscription),
                AlreadySubscribed = result.AlreadySubscribed
            };
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex);
        }
    }
}
