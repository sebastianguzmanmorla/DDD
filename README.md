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
  - **`ConfigureHandlerServicesGenerator`**: Registra automáticamente los manejadores de solicitudes (`IRequestHandler<TRequest, TResponse>`) y eventos (`INotificationHandler<TNotification>`).
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
