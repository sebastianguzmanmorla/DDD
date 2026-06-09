namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IRuleLocalization
{
    string Minimum(string label, int min);
    string Maximum(string label, int max);
    string NotNull(string label);
    string NotEmpty(string label);
    string AlreadyExists(string label);
    string Immutable(string label);
    string MaximumLength(string label, int length);
    string MinimumLength(string label, int length);
    string NotExists(string label);
    string NotValid(string label);
}
