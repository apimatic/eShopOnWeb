using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. POST /api/subscriptions — JWT authenticated.
/// Idempotent: ensures a single Maxio customer per shopper and does not duplicate an active
/// subscription on a repeat/double-click (returns 200 with the existing subscription instead of 201).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, CancellationToken>
{
    private readonly IMaxioBillingService _billing;
    private readonly ICurrentShopperService _currentShopper;

    public SubscribeEndpoint(IMaxioBillingService billing, ICurrentShopperService currentShopper)
    {
        _billing = billing;
        _currentShopper = currentShopper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, CancellationToken ct) => await HandleAsync(request, ct))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, CancellationToken ct)
    {
        try
        {
            var shopper = await _currentShopper.GetCurrentShopperAsync(ct);
            var result = await _billing.SubscribeAsync(shopper, request.PlanHandle ?? string.Empty, ct);

            var response = new SubscribeResponse
            {
                Subscription = result.Subscription,
                AlreadySubscribed = result.AlreadySubscribed
            };

            // Idempotent repeat → 200; a freshly created subscription → 201.
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
