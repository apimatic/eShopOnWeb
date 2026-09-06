using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionApiRequest request, IMaxioSubscriptionService service, HttpContext context, CancellationToken ct) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("sub")?.Value
                    ?? throw new UnauthorizedAccessException("User ID not found in token");
                return await HandleAsync(new CreateSubscriptionApiRequest(request.PlanHandle, userId), service);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, IMaxioSubscriptionService service)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var subscription = await service.CreateSubscriptionAsync(request.UserId, request.PlanHandle, CancellationToken.None);
            response.Subscription = subscription;
            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionApiRequest : BaseRequest
{
    public CreateSubscriptionApiRequest(string planHandle, string userId)
    {
        PlanHandle = planHandle;
        UserId = userId;
        _correlationId = Guid.NewGuid();
    }

    public string PlanHandle { get; }
    public string UserId { get; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
