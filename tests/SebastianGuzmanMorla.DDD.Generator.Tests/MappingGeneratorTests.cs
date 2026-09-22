using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SebastianGuzmanMorla.DDD.Testing;

namespace SebastianGuzmanMorla.DDD.Generator.Tests;

public sealed class MappingGeneratorTests
{
    [Fact]
    public void GeneratedMappings_ApplyNestedAndDuplicateNamesAndIgnoreUnboundGenericTemplates()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            namespace First
            {
                public class Customer { public string Name { get; set; } = ""; }
                public partial class Map : IEntityTypeConfiguration<Customer>
                {
                    public void Configure(EntityTypeBuilder<Customer> builder) => builder.Property(x => x.Name).HasMaxLength(73);
                }
                public partial class Map : IEntityTypeConfiguration<Customer> { }
            }
            namespace Second
            {
                public class Customer { public string Name { get; set; } = ""; }
                public class GenericMap<T> : IEntityTypeConfiguration<T> where T : class
                {
                    public virtual void Configure(EntityTypeBuilder<T> builder) { }
                }
                public class Container
                {
                    public class Map : GenericMap<Customer>
                    {
                        public override void Configure(EntityTypeBuilder<Customer> builder) => builder.Property(x => x.Name).HasMaxLength(91);
                    }
                }
            }
            """;
        GeneratorCompilation.Verify(source, new EntityTypeConfigurationGenerator(), assembly =>
        {
            var builder = new ModelBuilder(new ConventionSet());
            Type extensions = assembly.GetTypes().Single(t => t.Name == "ModelBuilderGeneratedExtensions");
            extensions.GetMethod("ApplyGeneratedConfigurations")!.Invoke(null, [builder]);
            Assert.Equal(2, builder.Model.GetEntityTypes().Count());
            Assert.Equal(73, builder.Model.FindEntityType(assembly.GetType("First.Customer")!)!.FindProperty("Name")!.GetMaxLength());
            Assert.Equal(91, builder.Model.FindEntityType(assembly.GetType("Second.Customer")!)!.FindProperty("Name")!.GetMaxLength());
        });
    }
}
