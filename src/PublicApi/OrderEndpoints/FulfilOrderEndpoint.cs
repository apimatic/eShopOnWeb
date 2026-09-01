using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator: marks the order fulfilled — this is when the held money is actually captured.
/// A stale authorization is renewed first; one that can no longer be renewed comes back as a
/// 409 the operator can act on. Repeating the call returns the existing capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, user);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ClaimsPrincipal user)
    {
        var payment = await _paymentService.FulfilOrderAsync(request.OrderId, CancellationToken.None);

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Fulfilled",
            Payment = PaymentDto.FromModel(payment)
        };
        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
