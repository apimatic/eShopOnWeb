using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Core.Request;

internal sealed class FormUrlEncodedRequest : IRequest
{
    private readonly IReadOnlyCollection<Param> _params;

    private FormUrlEncodedRequest(IReadOnlyCollection<Param> @params) => _params = @params;

    public HttpContent Get() =>
        new FormUrlEncodedContent(_params.SelectMany(ParameterFlattener.Flatten).ToList());

    public bool CanRetry => true;

    public static FormUrlEncodedRequest Create(IReadOnlyCollection<Param> @params) => new(@params);
}