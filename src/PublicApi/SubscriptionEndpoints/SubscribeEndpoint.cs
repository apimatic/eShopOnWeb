using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeated request for a plan the
/// user is already actively subscribed to returns the existing subscription (HTTP 200) rather
/// than creating a duplicate; a new subscription is created with HTTP 201.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Subscribes the caller to a plan", "Ensures a billing customer exists for the caller and enrolls them in the requested plan (idempotent)."));
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                title: "Invalid request",
                detail: "planHandle is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var subscriber = user.ToSubscriberIdentity();
        if (subscriber is null)
        {
            return SubscriptionErrorResults.MissingIdentity();
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle.Trim());

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Created = result.Created,
                Subscription = result.Subscription.ToDto()
            };

            return result.Created
                ? Results.Created($"api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionErrorResults.FromBillingException(ex);
        }
    }
}
