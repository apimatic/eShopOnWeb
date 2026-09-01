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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards — at PayPal and locally. Afterwards it is neither
/// listed nor usable to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService paymentMethodService, CancellationToken ct) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest(paymentMethodId, user.Identity?.Name ?? string.Empty),
                    paymentMethodService, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        return HandleAsync(request, paymentMethodService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService, CancellationToken ct)
    {
        try
        {
            await paymentMethodService.DeleteCardAsync(request.BuyerId, request.PaymentMethodId, ct);
            return Results.NoContent();
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId, string buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }

    public int PaymentMethodId { get; }
    public string BuyerId { get; }
}
