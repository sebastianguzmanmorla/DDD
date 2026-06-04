using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
#if !NET10_0_OR_GREATER
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
#endif
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.SmartEnum;

namespace SebastianGuzmanMorla.DDD.Transformers;

public sealed class ParametersTransformer<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
    string route,
    RequestMethod method
) : IOpenApiOperationTransformer
{
    private readonly string _route = route.TrimStart('/');

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken = default)
    {
        string relativePath = context.Description.RelativePath?.TrimStart('/') ?? string.Empty;

        if (!string.Equals(context.Description.HttpMethod, method.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(relativePath, _route, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (method == RequestMethod.Get || method == RequestMethod.Delete)
        {
            TransformGet(operation);
        }
        else
        {
            TransformPost(operation);
        }

        return Task.CompletedTask;
    }

#if NET10_0_OR_GREATER
    private static void TransformGet(OpenApiOperation operation)
    {
        operation.Parameters ??= [];

        foreach (PropertyInfo propertyInfo in typeof(T).GetProperties())
        {
            if (propertyInfo.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            string name = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                          ?? propertyInfo.Name;

            bool isRequired = IsExplicitlyRequired(propertyInfo);

            OpenApiSchema schema = MapToOpenApiSchema(propertyInfo.PropertyType, isRequired);

            OpenApiParameter parameter = new()
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = isRequired,
                Schema = schema
            };

            if (schema.Type == JsonSchemaType.Array)
            {
                parameter.Style = ParameterStyle.Form;
                parameter.Explode = true;
            }

            operation.Parameters.Add(parameter);
        }
    }

    private static void TransformPost(OpenApiOperation operation)
    {
        OpenApiSchema schema = new()
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(),
            Required = new HashSet<string>()
        };

        foreach (PropertyInfo prop in typeof(T).GetProperties())
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            string name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                          ?? prop.Name;

            bool required = IsExplicitlyRequired(prop);

            schema.Properties[name] = MapToOpenApiSchema(prop.PropertyType, required);

            if (required)
            {
                schema.Required.Add(name);
            }
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/x-www-form-urlencoded"] = new()
                {
                    Schema = schema
                }
            }
        };
    }

    private static OpenApiSchema MapToOpenApiSchema(Type originalType, bool isRequired)
    {
        bool isNullable = IsNullable(originalType, isRequired);
        Type type = Nullable.GetUnderlyingType(originalType) ?? originalType;

        if (type == typeof(Guid))
        {
            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.String | JsonSchemaType.Null
                    : JsonSchemaType.String,
                Format = "uuid",
                Example = JsonValue.Create("00000000-0000-0000-0000-000000000000")
            };
        }

        if (type == typeof(string))
        {
            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.String | JsonSchemaType.Null
                    : JsonSchemaType.String
            };
        }

        if (type == typeof(bool))
        {
            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.Boolean | JsonSchemaType.Null
                    : JsonSchemaType.Boolean
            };
        }

        if (type == typeof(int) ||
            type == typeof(long) ||
            type == typeof(short) ||
            type == typeof(uint) ||
            type == typeof(ulong))
        {
            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.Integer | JsonSchemaType.Null
                    : JsonSchemaType.Integer
            };
        }

        if (type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal))
        {
            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.Number | JsonSchemaType.Null
                    : JsonSchemaType.Number
            };
        }

        if (type.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = Enum
                    .GetNames(type)
                    .Select(n => JsonValue.Create(n))
                    .ToList<JsonNode>()
            };
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) &&
            type.IsGenericType &&
            type != typeof(string))
        {
            Type elementType = type.GetGenericArguments()[0];

            return new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = MapToOpenApiSchema(elementType, true)
            };
        }

        if (TryGetSmartEnumKeys(type, out IReadOnlyCollection<object>? values))
        {
            List<string> flagStrings = values.Select(v => v.ToString()!).ToList();

            string options = string.Join("|", flagStrings.Select(Regex.Escape));

            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.String | JsonSchemaType.Null
                    : JsonSchemaType.String,
                Enum = values
                    .Select(v => JsonValue.Create(v))
                    .Cast<JsonNode>()
                    .ToList(),
                Pattern = $"^({options})$",
                Description = $"Allowed values: {string.Join(", ", values.Select(v => v.ToString()))}",
                Example = JsonValue.Create(values.First())
            };
        }

        if (TryGetSmartEnumFlagsKeys(type, out IReadOnlyCollection<object>? flags))
        {
            List<string> flagStrings = flags.Select(f => f.ToString()!).ToList();

            string options = string.Join("|", flagStrings.Select(Regex.Escape));

            return new OpenApiSchema
            {
                Type = isNullable
                    ? JsonSchemaType.String | JsonSchemaType.Null
                    : JsonSchemaType.String,
                Description = $"Space separated values. Allowed: {string.Join(", ", flags)}",
                Example = JsonValue.Create(string.Join(" ", flags.Take(2))),
                Pattern = $"^({options})(\\s({options}))*$"
            };
        }

        return new OpenApiSchema
        {
            Type = JsonSchemaType.String
        };
    }
#else
    private static void TransformGet(OpenApiOperation operation)
    {
        operation.Parameters ??= [];

        foreach (PropertyInfo propertyInfo in typeof(T).GetProperties())
        {
            if (propertyInfo.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            string name = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                          ?? propertyInfo.Name;

            bool isRequired = IsExplicitlyRequired(propertyInfo);

            OpenApiSchema schema = MapToOpenApiSchema(propertyInfo.PropertyType, isRequired);

            OpenApiParameter parameter = new()
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = isRequired,
                Schema = schema
            };

            if (schema.Type == "array")
            {
                parameter.Style = ParameterStyle.Form;
                parameter.Explode = true;
            }

            operation.Parameters.Add(parameter);
        }
    }

    private static void TransformPost(OpenApiOperation operation)
    {
        OpenApiSchema schema = new()
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>(),
            Required = new HashSet<string>()
        };

        foreach (PropertyInfo prop in typeof(T).GetProperties())
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            string name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                          ?? prop.Name;

            bool required = IsExplicitlyRequired(prop);

            schema.Properties[name] = MapToOpenApiSchema(prop.PropertyType, required);

            if (required)
            {
                schema.Required.Add(name);
            }
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/x-www-form-urlencoded"] = new()
                {
                    Schema = schema
                }
            }
        };
    }

    private static OpenApiSchema MapToOpenApiSchema(Type originalType, bool isRequired)
    {
        bool isNullable = IsNullable(originalType, isRequired);
        Type type = Nullable.GetUnderlyingType(originalType) ?? originalType;

        if (type == typeof(Guid))
        {
            return new OpenApiSchema
            {
                Type = "string",
                Nullable = isNullable,
                Format = "uuid",
                Example = new OpenApiString("00000000-0000-0000-0000-000000000000")
            };
        }

        if (type == typeof(string))
        {
            return new OpenApiSchema
            {
                Type = "string",
                Nullable = isNullable
            };
        }

        if (type == typeof(bool))
        {
            return new OpenApiSchema
            {
                Type = "boolean",
                Nullable = isNullable
            };
        }

        if (type == typeof(int) ||
            type == typeof(long) ||
            type == typeof(short) ||
            type == typeof(uint) ||
            type == typeof(ulong))
        {
            return new OpenApiSchema
            {
                Type = "integer",
                Nullable = isNullable
            };
        }

        if (type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal))
        {
            return new OpenApiSchema
            {
                Type = "number",
                Nullable = isNullable
            };
        }

        if (type.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = "string",
                Enum = Enum
                    .GetNames(type)
                    .Select(n => (IOpenApiAny)new OpenApiString(n))
                    .ToList()
            };
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) &&
            type.IsGenericType &&
            type != typeof(string))
        {
            Type elementType = type.GetGenericArguments()[0];

            return new OpenApiSchema
            {
                Type = "array",
                Items = MapToOpenApiSchema(elementType, true)
            };
        }

        if (TryGetSmartEnumKeys(type, out IReadOnlyCollection<object>? values))
        {
            List<string> flagStrings = values.Select(v => v.ToString()!).ToList();

            string options = string.Join("|", flagStrings.Select(Regex.Escape));

            return new OpenApiSchema
            {
                Type = "string",
                Nullable = isNullable,
                Enum = values
                    .Select(v => (IOpenApiAny)new OpenApiString(v.ToString()!))
                    .ToList(),
                Pattern = $"^({options})$",
                Description = $"Allowed values: {string.Join(", ", values.Select(v => v.ToString()))}",
                Example = new OpenApiString(values.First().ToString()!)
            };
        }

        if (TryGetSmartEnumFlagsKeys(type, out IReadOnlyCollection<object>? flags))
        {
            List<string> flagStrings = flags.Select(f => f.ToString()!).ToList();

            string options = string.Join("|", flagStrings.Select(Regex.Escape));

            return new OpenApiSchema
            {
                Type = "string",
                Nullable = isNullable,
                Description = $"Space separated values. Allowed: {string.Join(", ", flags)}",
                Example = new OpenApiString(string.Join(" ", flags.Take(2))),
                Pattern = $"^({options})(\\s({options}))*$"
            };
        }

        return new OpenApiSchema
        {
            Type = "string"
        };
    }
#endif

    private static bool IsExplicitlyRequired(PropertyInfo propertyInfo)
    {
        return propertyInfo.GetCustomAttribute<RequiredAttribute>() is not null
               || propertyInfo.GetCustomAttribute<JsonRequiredAttribute>() is not null
               || propertyInfo.GetCustomAttribute<RequiredMemberAttribute>() is not null;
    }

    private static bool IsNullable(Type type, bool isRequired)
    {
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is not null;
        }

        return !isRequired;
    }

    private static bool TryGetSmartEnumKeys(Type type, [NotNullWhen(true)] out IReadOnlyCollection<object>? values)
    {
        values = null;

        Type? current = type;
        while (current is not null && current != typeof(object))
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SmartEnum<,>))
            {
                PropertyInfo keysProperty = current.GetProperty("Keys")!;
                object? result = keysProperty.GetValue(null);

                if (result is IEnumerable enumerable)
                {
                    values = enumerable.Cast<object>().ToList();
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool TryGetSmartEnumFlagsKeys(Type type, [NotNullWhen(true)] out IReadOnlyCollection<object>? values)
    {
        values = null;

        Type? current = type;
        while (current is not null && current != typeof(object))
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SmartEnumFlags<,,>))
            {
                Type enumType = current.GenericTypeArguments[1];
                return TryGetSmartEnumKeys(enumType, out values);
            }

            current = current.BaseType;
        }

        return false;
    }
}
