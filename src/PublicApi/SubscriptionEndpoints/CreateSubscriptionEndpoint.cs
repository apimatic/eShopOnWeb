using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan. Idempotent per shopper: repeating the request
/// returns the existing subscription instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionCaller, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, SubscriptionCaller caller, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, caller, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status202Accepted)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionCaller caller, ISubscriptionBillingService billing)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        if (caller.Account is null)
        {
            return BillingResultHttpMapping.UnknownCaller(response.CorrelationId());
        }

        var account = caller.Account with
        {
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim()
        };

        var result = await billing.SubscribeAsync(account, request.PlanHandle, caller.RequestAborted);
        if (!result.IsSuccess)
        {
            return result.ToFailureResult(response.CorrelationId());
        }

        response.Subscription = SubscriptionDto.From(result.Value!.Subscription);
        response.AlreadyExisted = result.Value.AlreadyExisted;
        return response.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
