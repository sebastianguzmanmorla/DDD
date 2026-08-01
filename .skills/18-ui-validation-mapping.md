# 18. UI Validation State Mapping (Razor Pages)

When validation results return to Blazor, controllers, or Razor Page models, populate UI view model state automatically.

## C# Extension Method (`PageModelExtensions`)
Converts `SebastianGuzmanMorla.Validator` results into `ModelState` errors:

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using SebastianGuzmanMorla.Validator;

namespace MyProject.Web.Extensions;

public static class PageModelExtensions
{
    public static PageResult AddValidationErrors(this PageModel pageModel, ValidationResult result)
    {
        foreach ((string field, List<string> errors) in result.Errors ?? [])
        {
            foreach (string error in errors)
            {
                // Clean property paths (e.g. removing JsonPath prefixes like "$.")
                pageModel.ModelState.AddModelError(field.Replace("$.", ""), error);
            }
        }

        return pageModel.Page();
    }
}
```

---

## PageModel Validation Interface Pattern
Declare binding properties on a shared validation interface so PageModels can pass `this` directly to validators:

1. **Define Interface**:
   ```csharp
   public interface ILoginValidation : IEntityValidation
   {
       string Email { get; }
       string Password { get; }
   }
   ```

2. **Implement Interface on PageModel**:
   ```csharp
   public class LoginModel(
       IValidator<ILoginValidation> validator,
       IServiceProvider serviceProvider
   ) : PageModel, ILoginValidation
   {
       [BindProperty]
       public string Email { get; set; } = "";

       [BindProperty]
       public string Password { get; set; } = "";

       public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
       {
           ValidationResult result = await validator.Validate(this, serviceProvider, cancellationToken);

           if (!result.IsValid)
           {
               return pageModel.AddValidationErrors(result);
           }

           return Redirect("/Home");
       }
   }
   ```
