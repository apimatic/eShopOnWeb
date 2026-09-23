using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent) and enrolls them (idempotent — a double-click never creates two customers/subscriptions),
/// returning the plan, price, state and next billing date.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, ISubscriptionBillingService billingService, HttpContext http) =>
            {
                return await HandleAsync(request ?? new SubscribeRequest(), billingService, http);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeResponse>(StatusCodes.Status202Accepted)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var subscriber = new SubscriberIdentity { BuyerId = buyerId, Email = buyerId };

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, http.RequestAborted);
            var dto = result.ToResponse();
            return result.Outcome switch
            {
                SubscribeOutcome.Created => Results.Created("api/my-subscriptions", dto),
                SubscribeOutcome.AlreadySubscribed => Results.Ok(dto),
                SubscribeOutcome.Pending => Results.Accepted("api/my-subscriptions", dto),
                SubscribeOutcome.Failed => Results.Json(dto, statusCode: StatusCodes.Status502BadGateway),
                _ => Results.Ok(dto)
            };
        }
        catch (UnknownSubscriptionPlanException ex)
        {
            return Results.BadRequest(new SubscriptionErrorResponse(ex.Message));
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
