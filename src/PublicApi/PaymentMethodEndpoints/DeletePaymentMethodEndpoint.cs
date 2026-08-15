using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; init; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;

    public DeletePaymentMethodRequest(int paymentMethodId) { PaymentMethodId = paymentMethodId; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among their
/// cards and can no longer be used to pay. Scoped to the owner.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, IPaymentMethodService paymentMethodService) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId) { BuyerId = buyerId }, paymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var deleted = await paymentMethodService.DeleteCardAsync(request.BuyerId, request.PaymentMethodId);
        if (!deleted) return Results.NotFound();
        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId()));
    }
}
