using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }
    public int PaymentMethodId { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among
/// their cards and can no longer be used to pay. Shopper-scoped to the caller's own cards.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, CallerId = PaymentEndpointHelpers.GetCallerId(user) },
                    service);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service)
    {
        await service.DeleteCardAsync(request.CallerId, request.PaymentMethodId);
        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId
        };
        return Results.Ok(response);
    }
}
