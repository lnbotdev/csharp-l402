namespace LnBot.L402;

public class L402BudgetExceededException : L402Exception
{
    public L402BudgetExceededException(string message) : base(message) { }
}
