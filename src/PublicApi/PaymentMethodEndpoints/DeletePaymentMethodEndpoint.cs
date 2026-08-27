using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, both locally and from PayPal's vault.
/// Afterwards it is no longer listed and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var request = new DeletePaymentMethodRequest(paymentMethodId)
                {
                    BuyerId = httpContext.User.Identity?.Name ?? string.Empty
                };
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        await paymentService.DeleteSavedCardAsync(request.PaymentMethodId, request.BuyerId, cancellationToken);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId,
            Deleted = true
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
    public string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}
