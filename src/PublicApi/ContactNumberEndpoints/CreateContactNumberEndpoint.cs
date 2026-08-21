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
            (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(request, contactNumberService, httpContext);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, contactNumberService, httpContext: null!);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService, HttpContext httpContext)
    {
        var buyerId = ShopperIdentity.TryGetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var created = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.PhoneNumber,
            NationalFormat = created.NationalFormat,
            CountryCode = created.CountryCode
        };

        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
