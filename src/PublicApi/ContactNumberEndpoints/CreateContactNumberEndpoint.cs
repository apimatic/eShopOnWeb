using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IShopperContactService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IShopperContactService contactService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, contactService, httpContext);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IShopperContactService contactService)
        => HandleAsync(request, contactService, null!);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IShopperContactService contactService, HttpContext httpContext)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var registered = await contactService.RegisterAsync(httpContext.GetRequiredBuyerId(), request.PhoneNumber, request.CountryCode);
        response.ContactNumberId = registered.Id;
        response.PhoneNumber = registered.PhoneNumber;
        response.NationalFormat = registered.NationalFormat;
        return Results.Created($"api/contact-numbers/{registered.Id}", response);
    }
}
