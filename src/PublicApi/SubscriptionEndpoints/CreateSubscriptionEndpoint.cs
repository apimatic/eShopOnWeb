using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent: a shopper who already holds a live subscription to the plan
/// gets that subscription back with a 200 instead of a second enrollment, and supplying
/// an idempotency key extends the same guarantee to concurrent retries.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var userName = CallerIdentity.GetUserName(user);
                if (userName is null)
                {
                    return Results.Unauthorized();
                }

                request.UserName = userName;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true)
            || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            var detail = validationResults.Count > 0
                ? string.Join(" ", validationResults.Select(r => r.ErrorMessage))
                : $"'{nameof(CreateSubscriptionRequest.PlanHandle)}' is required.";

            return Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeCommand
            {
                UserName = request.UserName,
                PlanHandle = request.PlanHandle.Trim(),
                FirstName = request.FirstName,
                LastName = request.LastName,
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // There is no read-one endpoint for a subscription; the shopper's collection is
        // where the new subscription can be observed.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
