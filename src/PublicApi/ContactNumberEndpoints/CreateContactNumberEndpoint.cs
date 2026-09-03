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
            (CreateContactNumberRequest request, IContactNumberService contactNumberService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, null!, contactNumberService);

    private async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        HttpContext httpContext,
        IContactNumberService contactNumberService)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var created = await contactNumberService.RegisterAsync(
            httpContext.GetBuyerId(),
            request.PhoneNumber,
            httpContext.RequestAborted);

        response.ContactNumberId = created.Id;
        response.PhoneNumber = created.CanonicalNumber;
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
