using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class ListPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary>Lists the signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.CurrentBuyerId(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = buyerId, Ct = ct }, service);
            })
            .Produces<List<SavedCardView>>()
            .WithTags("PayPalPaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService service)
    {
        var methods = await service.GetPaymentMethodsAsync(request.BuyerId, request.Ct);
        var views = methods
            .OrderByDescending(m => m.CreatedAt)
            .Select(PaymentMapping.ToView)
            .ToList();
        return Results.Ok(views);
    }
}
