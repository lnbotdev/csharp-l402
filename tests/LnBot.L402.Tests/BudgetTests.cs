using Xunit;

namespace LnBot.L402.Tests;

public class BudgetTests
{
    [Fact]
    public void Check_WithinBudget_DoesNotThrow()
    {
        var budget = new Budget(new L402ClientOptions { BudgetSats = 100, BudgetPeriod = BudgetPeriod.Day });

        budget.Check(50);
        budget.Record(50);
        budget.Check(50);
        budget.Record(50);

        // At limit — next should throw
        Assert.Throws<L402BudgetExceededException>(() => budget.Check(1));
    }

    [Fact]
    public void Check_ExceedsBudget_Throws()
    {
        var budget = new Budget(new L402ClientOptions { BudgetSats = 100, BudgetPeriod = BudgetPeriod.Day });
        budget.Record(80);

        Assert.Throws<L402BudgetExceededException>(() => budget.Check(21));
        // Exactly at limit should not throw
        budget.Check(20);
    }

    [Fact]
    public void Check_NoBudgetConfigured_NeverThrows()
    {
        var budget = new Budget(new L402ClientOptions());
        // Default BudgetSats is int.MaxValue — effectively unlimited
        budget.Check(999_999);
        budget.Record(999_999);
        budget.Check(999_999);
    }

    [Fact]
    public void ErrorMessage_ContainsSpentAndTotal()
    {
        var budget = new Budget(new L402ClientOptions { BudgetSats = 100, BudgetPeriod = BudgetPeriod.Day });
        budget.Record(80);

        var ex = Assert.Throws<L402BudgetExceededException>(() => budget.Check(30));
        Assert.Contains("80", ex.Message);
        Assert.Contains("100", ex.Message);
        Assert.Contains("30", ex.Message);
    }

    [Theory]
    [InlineData(BudgetPeriod.Hour)]
    [InlineData(BudgetPeriod.Day)]
    [InlineData(BudgetPeriod.Week)]
    [InlineData(BudgetPeriod.Month)]
    public void AllPeriodTypes_Work(BudgetPeriod period)
    {
        var budget = new Budget(new L402ClientOptions { BudgetSats = 10, BudgetPeriod = period });
        budget.Record(10);
        Assert.Throws<L402BudgetExceededException>(() => budget.Check(1));
    }
}
