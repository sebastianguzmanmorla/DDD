# SebastianGuzmanMorla.DDD

Una biblioteca base para implementar **Domain-Driven Design (DDD)** en .NET 10.0+ que integra patrones de Repositorio, Unit of Work, mensajería de Request/Response, y un conjunto de **Source Generators** de Roslyn para automatizar la configuración de dependencias de Entity Framework Core, Handlers y seguridad de datos sensibles.

## Características

- **Entidad Base Robusta**: Clase abstracta `Entity` con identificadores únicos `Guid` auto-generados con **UUID v7** (`Guid.CreateVersion7()`), control de auditoría temporal (`CreatedAt`, `UpdatedAt`) y soporte nativo para Eliminación Lógica (`DeletedAt` / Soft Delete).
- **Patrón Repository & Unit of Work**:
  - Implementación genérica y extensible sobre **Entity Framework Core**.
  - Soporte de transacciones y guardado automático o controlado (`UnitOfWork`).
  - Operaciones avanzadas como `Upsert`, `SoftDelete` y `HardDelete`.
  - Caché de repositorios mediante decoradores (`CachedRepository`).
- **Arquitectura de Mensajería (CQS)**:
  - Estructura base para solicitudes y respuestas: `Request<TResponse>` y `Response<TData>`.
  - Clases base de procesamiento `RequestHandler` y `RequestPageHandler` integradas con el motor de validación `SebastianGuzmanMorla.Validator`.
  - Soporte para notificaciones (`INotification`, `INotificationHandler`) y acciones post-commit.
- **Generadores de Código de Roslyn**:
  - **`ConfigureRepositoryServicesGenerator`**: Registra de forma automática en el contenedor de DI todos los repositorios que implementen `IRepository<TEntity>`.
  - **`ConfigureHandlerServicesGenerator`**: Registra automáticamente los manejadores de solicitudes (`IRequestHandler<TRequest, TResponse>`), eventos (`INotificationHandler<TNotification>`) y vinculadores de solicitudes (`IRequestBinder<TRequest, TErrorResponse>`).
  - **`ClearSensitivePropertiesGenerator`**: Genera automáticamente la implementación para limpiar datos sensibles (marcados con `[SensitiveData]`) de los objetos `Request` mediante el método `ClearSensitiveProperties()`.
  - **`EntityTypeConfigurationGenerator`**: Genera de forma automática métodos de extensión para aplicar todas las configuraciones de EF Core (`IEntityTypeConfiguration<T>`) al `ModelBuilder`.

---

## Instalación

Agrega las referencias de proyecto o instala el paquete NuGet (cuando esté publicado):

```bash
dotnet add package SebastianGuzmanMorla.DDD
```

---

## Uso y Componentes

### 1. Entidades y Auditoría

Define tus entidades heredando de `Entity`. La propiedad `Id` se inicializa automáticamente con **UUID v7**, idóneo para ordenación temporal y rendimiento en bases de datos:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;

public class Product : Entity
{
    public required string Name { get; set; }
    public decimal Price { get; set; }
}
```

### 2. Repositorios y Unit of Work

Define la interfaz de tu repositorio heredando de `IRepository<TEntity>`:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IProductRepository : IRepository<Product>
{
    Task<List<Product>> GetExpensiveProducts(decimal minPrice);
}
```

Implementa el repositorio heredando de `Repository<TContext, TEntity>`:

```csharp
using SebastianGuzmanMorla.DDD.Repositories;

public class ProductRepository(IServiceProvider serviceProvider) 
    : Repository<MyDbContext, Product>(serviceProvider), IProductRepository
```

#### Caché en Repositorio (`CachedRepository`)
Si necesitas habilitar almacenamiento en caché automático sobre Redis para tus entidades, hereda tu clase de repositorio de `CachedRepository<TContext, TEntity>` en lugar de `Repository`:

- Utiliza internamente `IConnectionMultiplexer` de Redis para leer/guardar los valores de forma automática.
- Invalida la caché de forma automática en operaciones de escritura (`Update`, `Upsert`, `SoftDelete`, `HardDelete`) registrando acciones post-commit en el `UnitOfWork`.
- Requiere especificar un prefijo de clave (`CacheKeyPrefix`), el tiempo de expiración opcional (`CacheExpiry`) y la información de metadatos de tipos para la serialización JSON (`JsonTypeInfo`).

```csharp
using SebastianGuzmanMorla.DDD.Repositories;
using System.Text.Json.Serialization.Metadata;

public class ProductRepository(IServiceProvider serviceProvider) 
    : CachedRepository<MyDbContext, Product>(serviceProvider), IProductRepository
{
    protected override string CacheKeyPrefix => "Product";
    
    protected override TimeSpan CacheExpiry => TimeSpan.FromMinutes(15);
    
    // Provee los metadatos de serialización para System.Text.Json
    protected override JsonTypeInfo<Product> JsonTypeInfo => MyJsonSerializerContext.Default.Product;
}
```

#### Registro Automático de Repositorios
Gracias al Source Generator de repositorios, solo debes declarar la siguiente clase parcial en tu capa de infraestructura:

```csharp
namespace MyProject.Infrastructure;

public static partial class ConfigureRepositoryServices
{
    public static IServiceCollection ConfigureInfrastructure(this IServiceCollection services)
    {
        // Este método parcial autogenerado se encarga de registrar todos los repositorios
        ConfigureGenerated(services);
        return services;
    }
}
```

### 3. Solicitudes, Respuestas y Manejadores (CQS)

Define tu Request y Response heredando de las clases base:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public class CreateProductRequest : Request<CreateProductResponse>
{
    public required string Name { get; set; }
    public decimal Price { get; set; }

    public override void ClearSensitiveProperties()
    {
        // Implementación generada automáticamente si usas partial y attributes
    }
}

public class CreateProductResponse : Response
{
    public Guid ProductId { get; set; }
}
```

#### Exclusión de Auditoría (`[LogIgnore]`)
Si deseas que una solicitud no genere logs de auditoría automáticos en tu infraestructura, decora el request con el atributo `[LogIgnore]`:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;

[LogIgnore]
public class SilentRequest : Request<Response>
{
    // ...
}
```

Escribe el Handler heredando de `RequestHandler<TContext, TRequest, TResponse>`:

```csharp
using SebastianGuzmanMorla.DDD.Handlers;

public class CreateProductHandler(IServiceProvider serviceProvider) 
    : RequestHandler<MyDbContext, CreateProductRequest, CreateProductResponse>(serviceProvider)
{
    protected override async Task<CreateProductResponse> Execute(
        CreateProductRequest request, 
        CancellationToken cancellationToken = default)
    {
        var product = new Product { Name = request.Name, Price = request.Price };
        await ServiceProvider.GetRequiredService<IProductRepository>().Add(cancellationToken, product);
        
        return new CreateProductResponse { ProductId = product.Id };
    }
}
```

#### Registro Automático de Handlers
Para registrar automáticamente todos tus manejadores (`IRequestHandler` y `INotificationHandler`), define la siguiente clase parcial:

```csharp
namespace MyProject.Application;

public static partial class ConfigureHandlerServices
{
    public static IServiceCollection ConfigureApplication(this IServiceCollection services)
    {
        // Generado automáticamente en compilación
        ConfigureGenerated(services);
        return services;
    }
}
```

### 4. Seguridad y Hashing de Contraseñas (ISecretHash)

La biblioteca proporciona abstracciones y utilidades para manejar de forma segura el hashing de secretos y contraseñas (por ejemplo, en el login/registro de usuarios):

- **`ISecretHash`**: Interfaz para entidades que contienen una contraseña cifrada.
- **`SecretHasher`**: Clase de utilidad estática que utiliza **Pbkdf2** con SHA256 para el hashing y verificación seguros.
- **`SecretHashExtensions`**: Métodos de extensión como `ValidateSecret` para facilitar la verificación del secreto.

#### Implementación de la Entidad
Define tu entidad heredando de `Entity` e implementando `ISecretHash`:

```csharp
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

public class User : Entity, ISecretHash
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? SecretHash { get; set; }
}
```

#### Hashing en el Registro
Al crear un usuario, genera el hash de la contraseña utilizando `SecretHasher.Hash`:

```csharp
using SebastianGuzmanMorla.DDD;

var user = new User
{
    Name = Name,
    Email = Email,
    SecretHash = SecretHasher.Hash(Password)
};

await userRepository.Add(cancellationToken, user);
```

#### Verificación en el Login
Para validar las credenciales de un usuario, utiliza el método de extensión `ValidateSecret`:

```csharp
using SebastianGuzmanMorla.DDD.Extensions;

User? user = await userRepository.FirstOrDefault(email, cancellationToken);

if (user is null || !user.ValidateSecret(password))
{
    // Credenciales inválidas
}
```

### 5. Localización de Reglas de Validación (`IRuleLocalization`)

Para la localización de mensajes de error de validación, la biblioteca proporciona la interfaz `IRuleLocalization` dentro de `SebastianGuzmanMorla.DDD.Domain.Interfaces`. Esta interfaz estandariza los mensajes de error comunes:

- `NotNull(string label)`
- `NotEmpty(string label)`
- `Maximum(string label, int max)`
- `AlreadyExists(string label)`
- `Immutable(string label)`
- `MaximumLength(string label, int length)`
- `MinimumLength(string label, int length)`
- `NotExists(string label)`
- `NotValid(string label)`

Ejemplo de uso en un validador:
```csharp
RuleFor(x => x.Name)
    .NotNull((x, _) => x.GetRequiredService<IRuleLocalization>().NotNull("Nombre"));
```

### 6. Integración con ASP.NET Core

La biblioteca incluye soporte integrado para simplificar el desarrollo de aplicaciones web ASP.NET Core:

#### A. Mapeo Automático de Enrutamientos Minimal API (`MapRequest`)
Permite mapear solicitudes (Requests) directamente a Minimal API endpoints utilizando el enfoque CQS sin necesidad de controladores redundantes. Soporta métodos HTTP (`GET`, `POST`, `PUT`, `DELETE`, `PATCH`) y maneja la deserialización correcta según el verbo (e.g. `[AsParameters]` para GET/DELETE y `[FromBody]` para otros).

##### Vinculación Personalizada de Peticiones (`IRequestBinder`)
Cuando un request requiere lógica de vinculación específica (por ejemplo, extraer valores de cabeceras personalizadas, cookies o multipart form data), se puede implementar la interfaz `IRequestBinder<TRequest, TErrorResponse>`:

```csharp
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public class CustomRequestBinder(IHttpContextAccessor httpContextAccessor) 
    : IRequestBinder<MyCustomRequest, Response>
{
    public async Task<(MyCustomRequest?, Response?)> BindAsync(CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        var value = context?.Request.Headers["X-Custom-Header"].ToString();
        
        if (string.IsNullOrEmpty(value))
        {
            return (null, new Response { Status = HttpStatusCode.BadRequest, Message = "Missing custom header" });
        }
        
        return (new MyCustomRequest { HeaderValue = value }, null);
    }
}
```

Esta implementación es detectada y registrada automáticamente por el generador de código de dependencias. Para usarla, se utiliza la sobrecarga de `MapRequest` de tres parámetros genéricos:

```csharp
group.MapRequest<MyCustomRequest, MyResponse, Response>(
    RequestMethod.Post,
    "/prefix",
    "/route",
    "CustomTag"
);
```

##### Respuestas de Archivos (`ResponseFile`)
Si un endpoint retorna un archivo, el Request correspondiente debe retornar un subtipo de `ResponseFile` (`ResponseFileByte` o `ResponseFilePath`). `MapRequest` gestionará automáticamente la respuesta como un archivo binario mediante `Results.File`:

```csharp
public class GetReportRequest : Request<ResponseFileByte> { ... }
```

```csharp
using SebastianGuzmanMorla.DDD.Extensions;

// En tu mapeador de Endpoints
public static void MapEndpoints(this RouteGroupBuilder group)
{
    // Mapea automáticamente el Request al Handler correspondiente y devuelve el Response formateado
    group.MapRequest<CreateProductRequest, CreateProductResponse>(
        CreateProductRequest.Method, 
        CreateProductRequest.Route, 
        "ProductsTag"
    );
}
```

#### B. Manejo Global de Excepciones (`ExceptionHandlerMiddleware`)
Un middleware global que captura excepciones, gestiona cancelaciones de peticiones (`TaskCanceledException`), y guarda los logs de errores generales en base de datos.

- Captura excepciones generales, las registra en la tabla `Log` de Entity Framework y devuelve un error 500 con un JSON estructurado que incluye el identificador único del log registrado (`LogId`) para facilitar su rastreo.
- Captura `BadHttpRequestException` y devuelve un código 400.
- Captura `TaskCanceledException` y devuelve un código 499 (Client Closed Request).

```csharp
using SebastianGuzmanMorla.DDD.Middleware;

// En tu Program.cs
app.UseMiddleware<ExceptionHandlerMiddleware<MyDbContext>>();
```

#### C. Health Checks en Caché con Redis (`MapCachedHealthChecks`)
Optimiza el monitoreo de salud del sistema ejecutando las pruebas de forma asíncrona mediante un servicio en segundo plano (`BackgroundService`) y guardando el reporte en caché en Redis, evitando sobrecargar las bases de datos u otros servicios externos con peticiones concurrentes al endpoint de `/health`.

1. Configura las opciones en el contenedor de servicios:
```csharp
builder.Services.AddOptions<SebastianGuzmanMorla.DDD.Domain.Options.CachedHealthCheckOptions>()
    .Configure(options =>
    {
        options.RedisKey = "MyProject:health";
        options.RedisLockKey = "MyProject:locks:health";
        options.CacheIntervalSeconds = 30; // Tiempo entre verificaciones
    });
```
2. Registra el servicio en segundo plano:
```csharp
builder.Services.AddSingleton<CachedHealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CachedHealthCheckService>());
```
3. Mapea la ruta de health check:
```csharp
app.MapCachedHealthChecks("/health"); // Opcional especificar ruta, por defecto /health
```

#### D. Autorización basada en Smart Enums (`SmartEnumRequirement`)
Permite validar permisos o alcances (scopes) de usuarios mediante políticas de autorización que verifican Flags utilizando la librería `SebastianGuzmanMorla.SmartEnum`.

1. Registra el manejador de requerimientos en DI:
```csharp
builder.Services.AddSingleton<IAuthorizationHandler, SmartEnumRequirementHandler<MyScopes, MyScope, string>>();
```
2. Configura tu política de autorización:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy =>
        policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new SmartEnumRequirement<MyScopes, MyScope, string>(MyScope.Administrator))
    );
});
```

#### E. Soporte para Documentación de OpenAPI/Swagger con Smart Enums (`ParametersTransformer`)
Un transformador de operaciones de OpenAPI (`IOpenApiOperationTransformer`) que permite documentar de forma automática los parámetros y propiedades de las solicitudes (especialmente en Minimal APIs), resolviendo los esquemas correctos para tipos como `Guid` y soportando de forma nativa los tipos de `SmartEnum` y `SmartEnumFlags`.

- Permite que las herramientas de documentación (como Scalar o Swagger) muestren automáticamente los valores permitidos y patrones de validación de los SmartEnums.
- Se configura agregando el transformador de operación en las opciones de OpenAPI en tu `Program.cs` o configuración de endpoints:

```csharp
using SebastianGuzmanMorla.DDD.Transformers;

builder.Services.AddOpenApi(options =>
{
    // Registra el transformador para las rutas correspondientes
    options.AddOperationTransformer(
        new ParametersTransformer<GetProductsRequest>(GetProductsRequest.Route, GetProductsRequest.Method)
    );
});
```


---

## Source Generators Incluidos

### 1. Limpieza de Datos Sensibles (`ClearSensitiveProperties`)
Marca las propiedades con `[SensitiveData]` y declara tu clase `Request` como `partial`. El generador escribirá la lógica de `ClearSensitiveProperties` por ti.

```csharp
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

public partial class LoginRequest : Request<LoginResponse>
{
    public required string Username { get; set; }

    [SensitiveData]
    public required string Password { get; set; }
}
```

### 2. Auto-Configuración de ModelBuilder (EF Core)
En tus clases de configuración de EF Core (`IEntityTypeConfiguration<T>`), el generador identificará cada clase y producirá un método de extensión en la clase `ModelBuilderGeneratedExtensions` dentro del espacio de nombres `Identity.Infrastructure`:

```csharp
// En tu DbContext simplemente llama a:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyGeneratedConfigurations();
}
```

---

## Requisitos

- **.NET 10.0** o superior
- **EF Core 10.0**
- **EFCore.BulkExtensions** (opcional para la optimización de `Upsert`)

## Licencia

Este proyecto está bajo la Licencia MIT. Ver el archivo [LICENSE](LICENSE) para más detalles.
