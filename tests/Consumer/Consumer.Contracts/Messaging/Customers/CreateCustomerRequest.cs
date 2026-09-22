using SebastianGuzmanMorla.DDD.Domain.Attributes;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace Consumer.Contracts.Messaging.Customers;

public partial class CreateCustomerRequest : Request<CreateCustomerResponse>
{
    public const string Route = "/customers";
    public const RequestMethod Method = RequestMethod.Post;
    public string? Name { get; set; }
    [SensitiveData] public string? Secret { get; set; }
}

public class CreateCustomerResponse : Response
{
    public Guid Id { get; set; }
}
