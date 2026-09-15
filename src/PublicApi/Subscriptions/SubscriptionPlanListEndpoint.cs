using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlanListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionBillingService service) => await HandleAsync(service))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionBillingService service)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("An HTTP context is required.");
        try
        {
            var result = await service.ListPlansAsync(Guid.NewGuid(), context.RequestAborted);
            return Results.Ok(result);
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription catalog is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

}
