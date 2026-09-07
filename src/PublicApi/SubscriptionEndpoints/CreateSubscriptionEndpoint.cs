using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionApiRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                try
                {
                    var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var subDto = await subscriptionService.CreateSubscriptionAsync(userId, request.ProductHandle);
                    var response = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        SubscriptionId = subDto.SubscriptionId,
                        CustomerId = subDto.CustomerId,
                        State = subDto.State,
                        ActivatedAt = subDto.ActivatedAt
                    };

                    return Results.Created($"api/subscriptions/{subDto.SubscriptionId}", response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, IMaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException();
    }
}
