using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Core;

internal sealed class UriFactory
{
    private readonly QueryParameterFactory _factory;
    private readonly TemplateParamsFactory _templateParamsFactory;

    public UriFactory(QueryParameterFactory factory, TemplateParamsFactory templateParamsFactory)
    {
        _factory = factory;
        _templateParamsFactory = templateParamsFactory;
    }

    public Uri Create(UrlTemplate urlTemplate, IReadOnlyCollection<Param> queryParameters,
        IReadOnlyCollection<TemplateParam> templateParams)
    {
        var hostPath = _templateParamsFactory.Create(urlTemplate, templateParams);

        if (queryParameters.Count == 0)
            return new Uri(hostPath);

        var queryString = _factory.Serialize(queryParameters);
        return new Uri($"{hostPath}?{queryString}");
    }
}