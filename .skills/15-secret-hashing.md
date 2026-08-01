# 15. Cryptographic Secret Hashing (`SecretHasher` / `ISecretHash`)

For secure password, client secret, and PIN storage, utilize transversal PBKDF2 salted key derivation utilities (SHA256 with 100,000 iterations).

---

## A. SecretHasher Direct Usage
Use `SecretHasher` to hash or verify raw secrets:

* **Hash Secret (when saving/creating)**:
  ```csharp
  using SebastianGuzmanMorla.DDD.Domain.Cryptography;

  string hashedPassword = SecretHasher.Hash("myPlainPassword");
  ```

* **Verify Secret (when authenticating)**:
  ```csharp
  bool isValid = SecretHasher.Verify("myPlainPassword", hashedPassword);
  ```

---

## B. Domain Entity Integration (`ISecretHash`)
If a domain entity implements `SebastianGuzmanMorla.DDD.Domain.Interfaces.ISecretHash`:

1. **Entity Definition**:
   ```csharp
   using SebastianGuzmanMorla.DDD.Domain.Entities;
   using SebastianGuzmanMorla.DDD.Domain.Interfaces;

   namespace MyProject.Domain.Entities;

   public class Client : Entity, ISecretHash
   {
       public required string Name { get; set; }
       public string? SecretHash { get; set; }
   }
   ```

2. **Setting Secret (when creating/updating entity)**:
   ```csharp
   clientEntity.SecretHash = SecretHasher.Hash(request.Secret);
   ```

3. **Verifying Secret Extension Method (`ValidateSecret`)**:
   ```csharp
   using SebastianGuzmanMorla.DDD.Domain.Extensions;

   // Extension method on ISecretHash instances
   bool isMatch = clientEntity.ValidateSecret(request.Secret);
   ```
