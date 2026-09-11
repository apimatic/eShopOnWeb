using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (ClaimsPrincipal user, [Microsoft.AspNetCore.Mvc.FromBody] CreateSubscriptionRequest req, IMaxioSubscriptionService service) =>
            {
                req.UserName = user.Identity?.Name ?? user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "unknown";
                return await HandleAsync(req, service);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService service)
    {
        var userName = request.UserName;
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            var result = await service.SubscribeAsync(userName, request.PlanHandle);
            response.SubscriptionId = result.SubscriptionId;
            response.CustomerId = result.CustomerId;
            response.PlanHandle = result.PlanHandle;
            response.State = result.State;
            response.NextBillingDate = result.NextBillingDate;
            response.Message = result.Message;
        }
        catch (System.Exception ex)
        {
            return Results.Problem(title: "Subscription creation failed", detail: ex.Message, statusCode: 502);
        }
        return Results.Ok(response);
    }
}
