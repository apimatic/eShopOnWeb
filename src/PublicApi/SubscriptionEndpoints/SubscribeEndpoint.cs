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

/// <summary>Enrolls the authenticated user in a plan (UC1). Mirrors CreateCatalogItemEndpoint.</summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/subscribe",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                // Server-assigned from the authenticated principal — never trust a client-supplied value.
                request.UserId = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(request.UserId, request.UserId, request.ProductHandle);
            response.Subscription = SubscriptionEndpointMappers.ToDto(subscription);
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (BillingConfigurationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Plan is not configured");
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
        }

        return Results.Ok(response);
    }
}
