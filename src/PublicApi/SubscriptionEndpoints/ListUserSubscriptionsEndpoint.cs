using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(new EmptyRequest(), subscriptionService, httpContext);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        await Task.Delay(10);
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = httpContext.User.FindFirst("given_name")?.Value ?? "User";
            var lastName = httpContext.User.FindFirst("family_name")?.Value ?? "";

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var customer = await subscriptionService.GetOrCreateCustomerAsync(email ?? userId, firstName, lastName, userId);
            if (customer?.Customer?.Id == null)
            {
                var response = new ListUserSubscriptionsResponse(request.CorrelationId());
                return Results.Ok(response);
            }

            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(customer.Customer.Id);
            var response2 = new ListUserSubscriptionsResponse(request.CorrelationId());

            if (subscriptions?.Subscriptions != null)
            {
                response2.Subscriptions.AddRange(subscriptions.Subscriptions.Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    CustomerId = s.CustomerId,
                    ProductId = s.ProductId,
                    ProductHandle = s.ProductHandle,
                    State = s.State,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt
                }));
            }

            return Results.Ok(response2);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse()
    {
    }

    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; } = new();
}
