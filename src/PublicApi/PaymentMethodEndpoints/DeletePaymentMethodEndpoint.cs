using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among their saved
/// cards and can no longer be used to pay. A shopper can only delete their own card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                var request = new DeletePaymentMethodRequest(paymentMethodId) { BuyerId = PaymentMapping.GetBuyerId(user) };
                return await HandleAsync(request, paymentService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId()) { PaymentMethodId = request.PaymentMethodId };
        await paymentService.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId);
        response.Deleted = true;
        return Results.Ok(response);
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId) => PaymentMethodId = paymentMethodId;
    public int PaymentMethodId { get; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}
