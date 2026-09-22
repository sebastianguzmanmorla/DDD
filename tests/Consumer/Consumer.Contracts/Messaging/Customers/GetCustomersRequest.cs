using Consumer.Contracts.Data.Customers;
using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace Consumer.Contracts.Messaging.Customers;

[LogIgnore]
public partial class GetCustomersRequest : RequestPage<GetCustomersResponse>, IPageValidation
{
    public const string Route = "/customers";
    public const RequestMethod Method = RequestMethod.Get;
}

public sealed class GetCustomersResponse : ResponsePage<CustomerData>;
