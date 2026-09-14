using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBillingService, IMapper mapper)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _mapper = mapper;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the caller to a plan",
        Description = "Ensures the caller has a customer record in the billing system and enrolls them on the requested plan. Repeating the call returns the existing subscription instead of creating a second one.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscriber = SubscriberResolver.FromPrincipal(User);
        if (subscriber is null)
        {
            return Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await _subscriptionBillingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A replay of an already satisfied request is not a fresh creation, so it does not answer 201.
        return result.AlreadySubscribed
            ? Ok(response)
            : Created($"api/subscriptions/{result.Subscription.Id}", response);
    }
}
