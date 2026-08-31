using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill with the provider for one of the caller's orders. The bill starts out
/// not yet put to the shopper. Returns the provider's invoice id.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint<IResult, CreateInvoiceForOrderRequest, IInvoiceService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateInvoiceForOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateInvoiceForOrderRequest request, IInvoiceService invoiceService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, invoiceService);
            })
            .Produces<CreateInvoiceForOrderResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateInvoiceForOrderRequest request, IInvoiceService invoiceService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await invoiceService.RaiseInvoiceForOrderAsync(request.OrderId, buyerId, request.DueDate);

        return ApiResults.From(result, view =>
        {
            var response = new CreateInvoiceForOrderResponse(request.CorrelationId())
            {
                InvoiceId = view.InvoiceId,
                Invoice = InvoiceDtoMapper.ToDto(view)
            };
            return Results.Created($"api/invoices/{view.InvoiceId}", response);
        });
    }
}
