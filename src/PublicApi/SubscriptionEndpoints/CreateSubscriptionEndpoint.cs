using System.Security.Claims;
using System.Threading;
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
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for the user and
/// enrolls them, idempotently — a double-click never creates a second customer or subscription.
/// The caller's identity comes from the JWT, never from the request body.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                // Identity comes from the token; overwrite anything the client may have sent.
                var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                request ??= new CreateSubscriptionRequest();
                request.UserReference = userName;
                request.Email = userName;

                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var command = new SubscribeRequest
        {
            UserReference = request.UserReference,
            Email = request.Email,
            PlanHandle = request.PlanHandle
        };

        try
        {
            var result = await billingService.SubscribeAsync(command, cancellationToken);
            response.Subscription = CustomerSubscriptionDto.FromModel(result.Subscription);
            response.AlreadyExisted = result.AlreadyExisted;
            return Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
