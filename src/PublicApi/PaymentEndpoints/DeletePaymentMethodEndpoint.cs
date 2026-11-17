using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards and disables it at Finix.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IPaymentService>
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
                (int paymentMethodId, IPaymentService paymentService) =>
                {
                    return await HandleAsync(paymentMethodId, paymentService);
                })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyerId(_httpContextAccessor);
        await paymentService.DeleteSavedCardAsync(buyerId, paymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Status = "Deleted"
        });
    }
}
