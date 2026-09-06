using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CreateSubscriptionRequest request, IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null || user.Email == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    var subscription = await maxioService.CreateSubscriptionAsync(userId, user.Email, request.PlanHandle);
                    var response = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        Subscription = new SubscriptionDto
                        {
                            Id = subscription.Id,
                            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                            PlanHandle = subscription.PlanHandle,
                            State = subscription.State,
                            NextBillingAt = subscription.NextBillingAt,
                            PriceInCents = subscription.PriceInCents
                        }
                    };

                    return Results.Created($"api/subscriptions/{subscription.Id}", response);
                }
                catch (Exception ex)
                {
                    var response = new CreateSubscriptionResponse(request.CorrelationId())
                    {
                        Error = ex.Message
                    };
                    return Results.BadRequest(response);
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            ;
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException("This method is not called directly");
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    public string? Error { get; set; }
}
