using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (SubscriptionBillingService service) => await HandleAsync(service))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionBillingService service)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("An HTTP context is required.");
        try
        {
            return Results.Ok(await service.ListMySubscriptionsAsync(
                context.User, Guid.NewGuid(), context.RequestAborted));
        }
        catch (SubscriptionRequestException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("Your subscriptions are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
