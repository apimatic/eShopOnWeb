using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards, at PayPal and locally. Afterwards it
/// no longer appears in the shopper's list and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly OrderPaymentService _paymentService;

    public DeletePaymentMethodEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await Handle(new DeletePaymentMethodRequest(paymentMethodId), user, ct);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user)
        => Handle(request, user, CancellationToken.None);

    private async Task<IResult> Handle(DeletePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var buyerId = user.Identity?.Name;
            if (buyerId is null)
            {
                return Results.Unauthorized();
            }

            await _paymentService.DeleteCardAsync(buyerId, request.PaymentMethodId, ct);
            return Results.NoContent();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
