namespace LnBot.L402;

public class L402PaymentFailedException : L402Exception
{
    public L402PaymentFailedException(string message) : base(message) { }
    public L402PaymentFailedException(string message, Exception inner) : base(message, inner) { }
}
