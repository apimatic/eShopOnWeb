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

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it is neither listed nor
/// usable to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService savedPaymentMethodService, HttpContext httpContext) =>
            {
                var buyerId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId, buyerId), savedPaymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService savedPaymentMethodService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());

        await savedPaymentMethodService.DeleteAsync(request.BuyerId, request.PaymentMethodId);

        response.Deleted = true;
        return Results.Ok(response);
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

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }

    public bool Deleted { get; set; }
}
