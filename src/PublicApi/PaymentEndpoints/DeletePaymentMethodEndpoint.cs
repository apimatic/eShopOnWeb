using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — removes a saved card; it can no longer pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, EmptyRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:guid}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid paymentMethodId, ISavedPaymentMethodService service) =>
                await PaymentEndpointSupport.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointSupport.RequireUserName(_http);
                    var ct = PaymentEndpointSupport.RequestAborted(_http);
                    await service.DeleteAsync(buyerId, paymentMethodId, ct);
                    return Results.NoContent();
                }))
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());
}
