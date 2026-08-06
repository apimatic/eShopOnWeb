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
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }
}

/// <summary>
/// Removes a saved card. Afterwards it no longer appears among the shopper's cards and can no longer
/// be used to pay. A shopper can only delete their own card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), paymentMethodService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var removed = await paymentMethodService.DeleteAsync(buyerId, request.PaymentMethodId);
        if (!removed) return Results.NotFound(new { message = $"Payment method {request.PaymentMethodId} was not found." });

        return Results.NoContent();
    }
}
