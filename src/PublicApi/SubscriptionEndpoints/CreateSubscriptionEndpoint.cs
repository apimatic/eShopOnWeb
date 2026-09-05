using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Idempotent: ensures a Maxio customer exists for the
/// caller (matched on their eShopOnWeb username) before enrolling, and returns the existing
/// active/trialing subscription on that plan instead of creating a duplicate on a retry.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var (firstName, lastName) = SplitDisplayName(request.Username);

        var subscription = await billingService.SubscribeAsync(
            customerReference: request.Username,
            customerEmail: request.Username,
            customerFirstName: firstName,
            customerLastName: lastName,
            planHandle: request.PlanHandle);

        response.Subscription = _mapper.Map<CustomerSubscriptionDto>(subscription);

        return Results.Ok(response);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string username)
    {
        var localPart = username.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Customer");
    }
}
