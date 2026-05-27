namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IDddLocalization
{
    string Minimum(string label, int min);
    string Maximum(string label, int max);
    string NotNull(string label);
    string NotEmpty(string label);
}
