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

/// <summary>
/// Subscribes the authenticated caller to a plan. Ensures a billing customer exists for the user and enrolls
/// them, idempotently (a double-click never creates a second customer or subscription). The caller's identity
/// comes from the JWT.
/// </summary>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, ICurrentSubscriberAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing, ICurrentSubscriberAccessor subscribers) =>
                await HandleAsync(request, billing, subscribers))
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ICurrentSubscriberAccessor subscribers)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(detail: "planHandle is required.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
        }

        var subscriber = await subscribers.GetCurrentAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billing.SubscribeAsync(subscriber, request.PlanHandle);
            response.Subscription = SubscriptionDto.From(subscription);
            return Results.Ok(response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Unknown plan");
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionResults.BillingError(ex);
        }
    }
}
