
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Restaurante.Api.Contracts;

public sealed class OpenApiSecurity : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Refeitório Inteligente";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
        };
        foreach (var group in context.DescriptionGroups)
        foreach (var description in group.Items)
        {
            if (!description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any() ||
                description.RelativePath is null || description.HttpMethod is null) continue;
            var path = "/" + description.RelativePath;
            if (document.Paths.TryGetValue(path, out var item) &&
                item.Operations?.TryGetValue(new HttpMethod(description.HttpMethod), out var operation) is true)
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
        }
        return Task.CompletedTask;
    }
}
