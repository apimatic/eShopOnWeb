using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>Handle of the plan, as returned by <c>GET api/subscription-plans</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;
    /// <summary>Optional; defaults to the part of the user name before the '@'.</summary>
    public string? FirstName { get; set; }
    /// <summary>Optional; defaults to "Customer".</summary>
    public string? LastName { get; set; }
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

    /// <summary>True when the caller already had this subscription; nothing new was created.</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Subscribe the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, string, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user.Identity?.Name ?? string.Empty, request, subscriptions, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(string userName, CreateSubscriptionRequest request, ISubscriptionService subscriptions) =>
        HandleAsync(userName, request, subscriptions, CancellationToken.None);

    public async Task<IResult> HandleAsync(string userName, CreateSubscriptionRequest request, ISubscriptionService subscriptions, CancellationToken cancellationToken)
    {
        var result = await subscriptions.SubscribeAsync(
            new SubscribeCommand(userName, request.PlanHandle, request.FirstName, request.LastName), cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed,
        };
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
