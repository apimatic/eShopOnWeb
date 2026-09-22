using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, EmptyRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService service) =>
                await PaymentEndpointSupport.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointSupport.RequireUserName(_http);
                    var ct = PaymentEndpointSupport.RequestAborted(_http);
                    var cards = await service.ListAsync(buyerId, ct);
                    return Results.Ok(cards);
                }))
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());
}
