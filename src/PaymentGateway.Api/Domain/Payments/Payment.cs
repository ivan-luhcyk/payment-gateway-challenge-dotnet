using System.Text.Json.Serialization;
using PaymentGateway.Api.Domain.Enums;

namespace PaymentGateway.Api.Domain.Payments;

public class Payment
{
    // Used exclusively by System.Text.Json during deserialisation from Redis.
    [JsonConstructor]
    public Payment(Guid id, PaymentStatus status, CardInfo card, Money money)
    {
        Id = id;
        Status = status;
        Card = card;
        Money = money;
    }

    public Guid Id { get; init; }
    public PaymentStatus Status { get; private set; }
    public CardInfo Card { get; init; }
    public Money Money { get; init; }

    /// <summary>Creates a new payment in Processing state.</summary>
    public static Payment Create(CardInfo card, Money money)
        => new(Guid.NewGuid(), PaymentStatus.Processing, card, money);

    public void Authorize() => Status = PaymentStatus.Authorized;
    public void Decline()   => Status = PaymentStatus.Declined;
    public void Fail()      => Status = PaymentStatus.Rejected;
}
