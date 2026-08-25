using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(string buyerId, int paymentMethodId)
    {
        BuyerId = buyerId;
        PaymentMethodId = paymentMethodId;
    }

    public string BuyerId { get; }
    public int PaymentMethodId { get; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it can no longer be listed or used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                var request = new DeletePaymentMethodRequest(user.Identity!.Name!, paymentMethodId);
                return await HandleAsync(request, savedCardService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        try
        {
            await savedCardService.DeletePaymentMethodAsync(request.BuyerId, request.PaymentMethodId);
        }
        catch (PaymentMethodNotFoundException)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId()));
    }
}
