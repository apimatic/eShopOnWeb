using System.Security.Claims;
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
/// Returns one of the caller's own bills: its current state, whatever the provider reports about how
/// it reached that state, and — once it has been put to the shopper — how they can pay it.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new GetInvoiceRequest(invoiceId, user.GetBuyerId()), invoiceService);
            })
            .Produces<GetInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoiceService invoiceService)
    {
        var view = await invoiceService.GetInvoiceAsync(request.InvoiceId, request.BuyerId);

        var response = new GetInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = view.InvoiceId,
            OrderId = view.OrderId,
            ProviderInvoiceId = view.ProviderInvoiceId,
            InvoiceNumber = view.InvoiceNumber,
            Status = view.Status.ToString(),
            ProviderStatus = view.ProviderStatus,
            Amount = view.Amount,
            Currency = view.CurrencyCode,
            DueDate = view.DueDate,
            CustomerName = view.CustomerName,
            CustomerEmail = view.CustomerEmail,
            CreatedAt = view.CreatedAt,
            IssuedAt = view.IssuedAt,
            WithdrawnAt = view.WithdrawnAt,
            History = InvoiceMapping.ToEventDtos(view.History),
            PaymentLink = view.PaymentLink
        };

        return Results.Ok(response);
    }
}
