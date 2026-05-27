namespace SebastianGuzmanMorla.DDD.Domain.Extensions;

public static class TypeExtensions
{
    public static string GetFormattedName(this Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string genericArguments = type
            .GetGenericArguments()
            .Select(x => x.Name)
            .Aggregate((x1, x2) => $"{x1}, {x2}");

        return $"{type.Name[..type.Name.IndexOf('`')]}<{genericArguments}>";
    }
}
