using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    internal int PaymentMethodId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the caller's
/// cards and can no longer be used to pay (it is also deleted from PayPal's Vault).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService service) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest
                    {
                        PaymentMethodId = paymentMethodId,
                        BuyerId = user.Identity?.Name ?? string.Empty
                    }, service);
            })
            .Produces<DeletePaymentMethodResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service)
    {
        var deleted = await service.DeleteCardAsync(request.BuyerId, request.PaymentMethodId);
        if (!deleted)
        {
            return Results.NotFound();
        }

        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId,
            Deleted = true
        };
        return Results.Ok(response);
    }
}
