using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, SubscriptionBillingService service) => await HandleAsync(request, service))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionBillingService service)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("An HTTP context is required.");
        try
        {
            return Results.Ok(await service.SubscribeAsync(
                context.User, request, Guid.NewGuid(), context.RequestAborted));
        }
        catch (SubscriptionRequestException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription could not be completed with the billing provider.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
