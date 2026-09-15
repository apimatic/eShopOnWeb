using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, service, httpContext, ct);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, null!, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        IContactNumberService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var buyerId = EndpointIdentity.GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contact = await service.RegisterAsync(buyerId, request.PhoneNumber, ct);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contact.Id,
            CanonicalNumber = contact.CanonicalNumber
        };
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
