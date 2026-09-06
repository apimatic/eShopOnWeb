using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan. The billing customer is created on first use, and the call is
/// idempotent per shopper and plan: repeating it returns the subscription that already exists
/// rather than enrolling the shopper twice.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                // The shopper always comes from the token, never from the body.
                request.UserName = userName;

                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the signed-in shopper to a plan",
                description: "Ensures a billing customer exists for the caller and enrolls them on the requested plan. " +
                             "Returns 201 when the subscription was created and 200 when the shopper already held it."));
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var command = new SubscribeCommand(request.UserName, request.PlanHandle?.Trim() ?? string.Empty)
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Organization = request.Organization
        };

        var result = await billingService.SubscribeAsync(command, cancellationToken);

        response.Subscription = result.Subscription.ToDto();
        response.Created = result.Created;

        if (!result.Created)
        {
            return Results.Ok(response);
        }

        return Results.Created("api/my-subscriptions", response);
    }
}
