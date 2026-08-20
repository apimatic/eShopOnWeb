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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext http, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(request, http, contactNumbers);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, null!, contactNumbers);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext http, IContactNumberService contactNumbers)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var created = await contactNumbers.RegisterAsync(http.GetRequiredBuyerId(), request.PhoneNumber);
        response.ContactNumberId = created.Id;
        response.PhoneNumber = created.PhoneNumber;
        response.NationalFormat = created.NationalFormat;
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
