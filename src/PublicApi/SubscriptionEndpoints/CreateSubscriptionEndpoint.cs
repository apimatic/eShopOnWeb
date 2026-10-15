using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    /// <summary>True when the caller was already subscribed to this plan (idempotent repeat).</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Subscribes the authenticated shopper to a plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, SubscriptionCaller>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService service, SubscriptionCaller caller, CancellationToken ct) =>
            {
                return await HandleAsync(request, service, caller, ct);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service, SubscriptionCaller caller)
        => HandleAsync(request, service, caller, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service, SubscriptionCaller caller, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        var identity = await caller.ResolveAsync();
        if (identity is null)
            return Results.Unauthorized();

        var result = await service.SubscribeAsync(identity.UserId, identity.UserName, identity.Email, request.PlanHandle.Trim(), ct);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed,
        };
        return result.AlreadySubscribed ? Results.Ok(response) : Results.Created("api/my-subscriptions", response);
    }
}
