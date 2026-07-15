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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records metered usage against a subscription (UC2). Any authenticated user may report usage against
/// their own subscription; a member of the Administrators role may report usage against any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserId = user.Identity?.Name;
                request.ActingAsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        try
        {
            var reading = await subscriptionService.RecordUsageAsync(
                request.UserId, request.ActingAsAdmin, request.SubscriptionId, request.Quantity, request.Memo);

            response.Recorded = reading.Recorded;
            response.PeriodToDateUnits = reading.PeriodToDateUnits;
            response.PeriodToDateAvailable = reading.PeriodToDateAvailable;
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (SubscriptionNotFoundException ex)
        {
            return Results.NotFound(ex.Message);
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            return Results.Conflict(ex.Message);
        }
        catch (BillingConfigurationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Metered component is not configured");
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
        }

        return Results.Ok(response);
    }
}
