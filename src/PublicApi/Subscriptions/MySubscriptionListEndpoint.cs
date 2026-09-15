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

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext context, SubscriptionService service, CancellationToken cancellationToken) =>
            {
                var userName = context.User.FindFirstValue(ClaimTypes.Name);
                if (userName is null)
                    return Results.Unauthorized();

                try
                {
                    return Results.Ok(await service.ListMySubscriptionsAsync(userName, cancellationToken));
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
            .Produces<IReadOnlyList<SubscriptionDto>>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService request)
    {
        throw new NotSupportedException("The route handler is the endpoint implementation.");
    }
}
