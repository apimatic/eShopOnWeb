using System.Security.Claims;
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
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call for the same plan
/// returns the existing subscription rather than creating a duplicate customer or subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly SubscriptionBillingService _billingService;

    public SubscribeEndpoint(SubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    Task<IResult> IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>.HandleAsync(SubscribeRequest request, ClaimsPrincipal user) =>
        HandleAsync(request, user, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        try
        {
            var result = await _billingService.SubscribeAsync(user, request.ProductHandle, request.FirstName, request.LastName, cancellationToken);
            response.Subscription = result.Subscription;
            response.AlreadyExisted = result.AlreadyExisted;

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (UnknownSubscriptionPlanException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return Results.UnprocessableEntity(new { message = ex.Message, errors = ex.Errors });
        }
        catch (MaxioApiException ex)
        {
            return ListSubscriptionPlansEndpoint.MaxioError(ex);
        }
    }
}
