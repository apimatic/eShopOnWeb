using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds.
/// A stale authorization is renewed first; one that cannot be renewed yields an
/// actionable error.
/// </summary>
public class FulfilOrderEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<FulfilOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Fulfils an order and captures the payment",
        Description = "Operator-only. Captures the held funds; the response shows the captured amount, PayPal's fee and the net proceeds.",
        OperationId = "orders.fulfil",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<FulfilOrderResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var orderId = int.Parse((string)RouteData.Values["orderId"]!);
        try
        {
            var payment = await _orderPaymentService.FulfilOrderAsync(orderId, cancellationToken);
            return new FulfilOrderResponse
            {
                OrderId = orderId,
                OrderStatus = "Fulfilled",
                Payment = PaymentDto.FromPayment(payment)
            };
        }
        catch (OrderNotFoundException)
        {
            return NotFound();
        }
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public FulfilOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
