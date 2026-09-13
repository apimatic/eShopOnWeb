using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionMyListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                var email = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                var request = new MySubscriptionsRequest { Email = email };
                return await HandleAsync(request, maxioService);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioService maxioService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await maxioService.ListSubscriptionsByEmailAsync(request.Email);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            PlanName = s.PlanName,
            PlanHandle = s.PlanHandle,
            Price = s.Price,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingDate = s.NextBillingDate,
            CreatedAt = s.CreatedAt
        }));

        return Results.Ok(response);
    }
}
