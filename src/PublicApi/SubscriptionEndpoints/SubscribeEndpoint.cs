using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a double-click never creates a
/// second billing customer or a duplicate active subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly SubscriberResolver _subscriberResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(SubscriberResolver subscriberResolver, IHttpContextAccessor httpContextAccessor)
    {
        _subscriberResolver = subscriberResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the shopper to a plan",
                description: "Ensures a billing customer exists for the shopper and enrolls them in the plan (idempotent)."));
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscriber = await _subscriberResolver.ResolveAsync(_httpContextAccessor.HttpContext!.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);
            response.Subscription = result.Subscription.ToDto();
            response.AlreadyExisted = result.AlreadyExisted;

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/subscriptions/{result.Subscription.Id}", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Json(
                new { statusCode = StatusCodes.Status404NotFound, message = ex.Message, errors = ex.Errors },
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (SubscriptionException ex)
        {
            return Results.Json(
                new { statusCode = StatusCodes.Status400BadRequest, message = ex.Message, errors = ex.Errors },
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MaxioApiException ex)
        {
            // The billing system rejected the request; surface as an upstream error.
            return Results.Json(
                new { statusCode = StatusCodes.Status502BadGateway, message = ex.Message, errors = ex.Errors },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
