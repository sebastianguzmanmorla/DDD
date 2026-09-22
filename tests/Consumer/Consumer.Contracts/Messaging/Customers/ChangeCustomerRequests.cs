using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace Consumer.Contracts.Messaging.Customers;

public partial class RenameCustomerRequest : Request<Response>
{
    public const string Route = "/customers/{id}";
    public const RequestMethod Method = RequestMethod.Put;
    public Guid Id { get; set; }
    public required string Name { get; set; }
}

public partial class DeleteCustomerRequest : Request<Response>
{
    public const string Route = "/customers/{id}";
    public const RequestMethod Method = RequestMethod.Delete;
    public Guid Id { get; set; }
}
