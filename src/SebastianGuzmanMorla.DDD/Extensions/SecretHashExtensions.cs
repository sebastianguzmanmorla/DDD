using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Extensions;

public static class SecretHashExtensions
{
    public static bool ValidateSecret(this ISecretHash entity, string secret)
    {
        return entity.SecretHash is not null && SecretHasher.Verify(secret, entity.SecretHash);
    }
}
