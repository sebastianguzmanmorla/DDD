using SebastianGuzmanMorla.Validator.Interfaces;

namespace SebastianGuzmanMorla.DDD.Domain.Interfaces;

public interface IPageValidation : IEntityValidation
{
    int? Page { get; }

    int? Size { get; }
}
