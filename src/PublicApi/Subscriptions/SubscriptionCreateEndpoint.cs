using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, HttpContext context, SubscriptionService service, CancellationToken cancellationToken) =>
            {
                var userName = context.User.FindFirstValue(ClaimTypes.Name);
                if (userName is null)
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { "A plan handle is required." } });

                try
                {
                    var subscription = await service.SubscribeAsync(userName, request.PlanHandle, cancellationToken);
                    return Results.Ok(subscription);
                }
                catch (SubscriptionPlanNotFoundException)
                {
                    return Results.NotFound(new { message = "The requested subscription plan was not found." });
                }
                catch (SubscriptionUserNotFoundException)
                {
                    return Results.Unauthorized();
                }
                catch (MaxioConfigurationException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (MaxioApiException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service)
    {
        throw new NotSupportedException("The route handler is the endpoint implementation.");
    }
}

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}
