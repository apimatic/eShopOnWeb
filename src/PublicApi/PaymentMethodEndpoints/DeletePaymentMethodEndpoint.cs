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
    public string BuyerId { get; set; } = "";
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DeletePaymentMethodResponse()
    {
    }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>Removes a saved card; afterwards it can no longer be listed or used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var request = new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = user.Identity!.Name! };
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());
        await paymentMethodService.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        response.PaymentMethodId = request.PaymentMethodId;
        response.Deleted = true;
        return Results.Ok(response);
    }
}
