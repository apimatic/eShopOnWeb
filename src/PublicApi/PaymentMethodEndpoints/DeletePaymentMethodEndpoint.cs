using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Remove one of the signed-in shopper's saved cards and disable it at Finix.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, PaymentService>
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
            (int paymentMethodId, PaymentService paymentService) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), paymentService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, PaymentService paymentService)
    {
        try
        {
            var ownerId = EndpointHelpers.GetOwnerId(_httpContextAccessor.HttpContext!.User);
            await paymentService.DeleteSavedCardAsync(ownerId, request.PaymentMethodId);

            return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = request.PaymentMethodId,
                Status = "Deleted"
            });
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
