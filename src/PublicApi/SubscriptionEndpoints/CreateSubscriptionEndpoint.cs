using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, IMapper mapper)
    {
        _subscriptionService = subscriptionService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                try
                {
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                 user.FindFirst("unique_name")?.Value ??
                                 user.FindFirst("sub")?.Value;
                    var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ??
                                    user.FindFirst("unique_name")?.Value;

                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
                    {
                        return Results.Unauthorized();
                    }

                    var firstName = user.FindFirst("first_name")?.Value;
                    var lastName = user.FindFirst("last_name")?.Value;

                    var subscription = await _subscriptionService.CreateSubscriptionAsync(
                        userId, userEmail, firstName, lastName, request.PlanHandle);

                    response.Subscription = _mapper.Map<UserSubscriptionDto>(subscription);
                    response.Success = true;
                    response.Message = "Subscription created successfully";
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.Message = $"Error creating subscription: {ex.Message}";
                    return Results.BadRequest(response);
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException("Use AddRoute instead");
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
