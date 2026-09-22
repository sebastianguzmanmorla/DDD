using Consumer.Application;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain;
using Consumer.Domain.Entities.Customers;
using Consumer.Domain.Interfaces.Repositories;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.Validator.Interfaces;

var request = new CreateCustomerRequest { Name = "Package consumer", Secret = "private" };
request.ClearSensitiveProperties();
if (request.Secret is not null || request.Name != "Package consumer")
    throw new InvalidOperationException("Packaged redaction generator failed.");

var services = new ServiceCollection().ConfigureDomain().ConfigureInfrastructure(_ => { }).ConfigureApplication();
Type[] required = [typeof(ICustomerRepository), typeof(IRequestHandler<CreateCustomerRequest, CreateCustomerResponse>), typeof(IValidator<GetCustomersRequest>)];
foreach (var service in required)
    if (!services.Any(d => d.ServiceType == service))
        throw new InvalidOperationException($"Packaged registration generator omitted {service}.");

var builder = new ModelBuilder(new ConventionSet());
builder.ApplyGeneratedConfigurations();
if (builder.Model.FindEntityType(typeof(Customer))?.FindProperty(nameof(Customer.Name))?.GetMaxLength() != 100)
    throw new InvalidOperationException("Packaged mapping generator failed.");

Console.WriteLine($"Packaged consumer verified on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
