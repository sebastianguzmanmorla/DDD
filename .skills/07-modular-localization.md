# 7. Modular Localization

The application uses an interface-driven, modular approach to localization. Strings are grouped by feature area rather than in a single monolith.

## A. Core Localization Rules (APIs / Handlers)
When constructing messages (e.g., error results, validations), **never pass hardcoded string literals** for entity names or labels. Pass localized properties of `GeneralLocalization` instead.
* **❌ Incorrect**: `Message = RuleLocalization.NotExists("User");`
* **✅ Correct**: `Message = RuleLocalization.NotExists(GeneralLocalization.User);`

---

## B. Resource Marker Class Pattern
In ASP.NET Core, `IStringLocalizer<TResource>` uses the type name and namespace of `TResource` to resolve matching `.resx` resource files (e.g., `MyProject.Localization.Resources.MyModuleResource.resx`).

1. **Create Resource Files**: Place resource files under `[Project].Localization/Resources` (e.g., `MyModuleResource.resx` and `MyModuleResource.es-CL.resx`).
2. **Define Resource Marker Class**: Create a class file `MyModuleResource.cs` matching the resource name:
   ```csharp
   namespace MyProject.Localization.Resources;

   public class MyModuleResource
   {
   }
   ```
3. **Inject `IStringLocalizer<TResource>`**:
   ```csharp
   using Microsoft.Extensions.Localization;
   using MyProject.Contracts.Interfaces.Localization;
   using MyProject.Localization.Resources;

   namespace MyProject.Localization;

   public class MyModuleLocalization(
       IStringLocalizer<MyModuleResource> localizer
   ) : IMyModuleLocalization
   {
       public string Title => localizer[nameof(Title)];
   }
   ```

---

## C. Project Localization Configuration (Bootstrap)

1. **Service Registration (`Program.cs`)**:
   ```csharp
   builder.Services
       .AddLocalization()
       .ConfigureLocalization();

   builder.Services.Configure<RequestLocalizationOptions>(options =>
   {
       List<CultureInfo> supportedCultures = [ new("en-US"), new("es-CL") ];
       options.DefaultRequestCulture = new RequestCulture("en-US", "en-US");
       options.SupportedCultures = supportedCultures;
       options.SupportedUICultures = supportedCultures;
       options.ApplyCurrentCultureToResponseHeaders = true;
   });
   ```

2. **Middleware Activation (`Program.cs`)**:
   ```csharp
   RequestLocalizationOptions localizationOptions =
       app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

   app.UseRequestLocalization(localizationOptions);
   ```

3. **Project File Configuration (`[Project].Localization.csproj`)**:
   ```xml
   <ItemGroup>
       <EmbeddedResource Update="Resources\MyModuleResource.resx">
           <Generator>PublicResXFileCodeGenerator</Generator>
           <LastGenOutput>MyModuleResource.Designer.cs</LastGenOutput>
       </EmbeddedResource>
       <Compile Update="Resources\MyModuleResource.Designer.cs">
           <DesignTime>True</DesignTime>
           <AutoGen>True</AutoGen>
           <DependentUpon>MyModuleResource.resx</DependentUpon>
       </Compile>
       <EmbeddedResource Update="Resources\MyModuleResource.es-CL.resx">
           <DependentUpon>MyModuleResource.resx</DependentUpon>
       </EmbeddedResource>
   </ItemGroup>
   ```
