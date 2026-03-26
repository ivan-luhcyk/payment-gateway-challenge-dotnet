using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PaymentGateway.Api.Presentation.Swagger;

public class IdempotencyHeaderOperationFilter : IOperationFilter
{
    private readonly string _headerName;

    public IdempotencyHeaderOperationFilter(IOptions<IdempotencyOptions> options)
    {
        _headerName = options.Value.HeaderName;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.ApiDescription.RelativePath, "api/Payments", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name        = _headerName,
            In          = ParameterLocation.Header,
            Required    = true,
            Description = "Unique key used to deduplicate retries of POST /api/payments.",
            Schema      = new OpenApiSchema { Type = "string" }
        });
    }
}
