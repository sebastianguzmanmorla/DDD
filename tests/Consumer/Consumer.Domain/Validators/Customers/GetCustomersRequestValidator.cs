using Consumer.Contracts.Messaging.Customers;
using SebastianGuzmanMorla.Validator;

namespace Consumer.Domain.Validators.Customers;

// The Validator generator composes IPageValidation rules into this validator.
public partial class GetCustomersRequestValidator : Validator<GetCustomersRequest>;
