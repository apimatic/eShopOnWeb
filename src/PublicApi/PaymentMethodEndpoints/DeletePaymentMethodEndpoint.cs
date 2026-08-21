using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    [JsonIgnore] public int PaymentMethodId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among the
/// caller's saved cards and can no longer be used to pay. Shopper-scoped.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, CallerId = user.GetUserName() }, service, ct);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service, CancellationToken ct)
    {
        var removed = await service.DeleteCardAsync(request.PaymentMethodId, request.CallerId, ct);
        return removed
            ? Results.NoContent()
            : Results.NotFound(new { message = "The saved card was not found." });
    }
}
