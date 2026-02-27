namespace LnBot.L402;

public class L402Exception : Exception
{
    public L402Exception(string message) : base(message) { }
    public L402Exception(string message, Exception inner) : base(message, inner) { }
}
