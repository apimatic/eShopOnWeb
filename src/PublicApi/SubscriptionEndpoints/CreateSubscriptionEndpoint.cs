using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a backing Maxio customer exists and is
/// idempotent per user: a double-click never creates a second customer or a duplicate subscription.
/// The subscriber identity comes from the JWT, not the request body.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                if (!SubscriberIdentity.TryResolve(user, out var reference, out var email, out var firstName, out var lastName))
                {
                    return Results.Unauthorized();
                }

                request.UserReference = reference;
                request.Email = email;
                request.FirstName = firstName;
                request.LastName = lastName;

                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "A 'planHandle' is required. Call GET /api/subscription-plans to see available plans.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscription request");
        }

        var subscribeRequest = new SubscribeRequest
        {
            UserReference = request.UserReference,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PlanHandle = request.PlanHandle.Trim()
        };

        try
        {
            var result = await billingService.SubscribeAsync(subscribeRequest);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                CustomerId = result.CustomerId,
                CustomerReference = result.CustomerReference,
                AlreadyExisted = result.AlreadyExisted
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return BillingResults.Problem(ex);
        }
    }
}
