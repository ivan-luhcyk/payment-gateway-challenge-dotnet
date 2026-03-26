using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Tests.Unit.Domain;

public class PaymentTests
{
    private static readonly CardInfo Card = new(8877, 4, 2030);
    private static readonly Money Money = new(1050, "GBP");

    [Fact]
    public void Create_SetsProcessingStatus()
    {
        var payment = Payment.Create(Card, Money);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    [Fact]
    public void Create_AssignsNonEmptyId()
    {
        var payment = Payment.Create(Card, Money);
        Assert.NotEqual(Guid.Empty, payment.Id);
    }

    [Fact]
    public void Create_TwoPayments_HaveDifferentIds()
    {
        var p1 = Payment.Create(Card, Money);
        var p2 = Payment.Create(Card, Money);
        Assert.NotEqual(p1.Id, p2.Id);
    }

    [Fact]
    public void Create_SetsCardInfo()
    {
        var payment = Payment.Create(Card, Money);
        Assert.Equal(Card, payment.Card);
    }

    [Fact]
    public void Create_SetsMoney()
    {
        var payment = Payment.Create(Card, Money);
        Assert.Equal(Money, payment.Money);
    }

    [Fact]
    public void Authorize_ChangesStatusToAuthorized()
    {
        var payment = Payment.Create(Card, Money);
        payment.Authorize();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Fact]
    public void Decline_ChangesStatusToDeclined()
    {
        var payment = Payment.Create(Card, Money);
        payment.Decline();
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public void Fail_ChangesStatusToRejected()
    {
        var payment = Payment.Create(Card, Money);
        payment.Fail();
        Assert.Equal(PaymentStatus.Rejected, payment.Status);
    }
}
