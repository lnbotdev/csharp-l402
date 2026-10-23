namespace LnBot.L402;

/// <summary>Options for the L402 auto-pay HTTP handler.</summary>
public class L402ClientOptions
{
    /// <summary>LnBot wallet ID to use for L402 payments.</summary>
    public string WalletId { get; set; } = "";

    /// <summary>Max sats to pay for a single request, read from the invoice itself. Default: 1000.</summary>
    public int MaxPrice { get; set; } = 1000;

    /// <summary>Total budget in sats for the period, kept in memory (it restarts with the process). Default: unlimited.</summary>
    public int BudgetSats { get; set; } = int.MaxValue;

    /// <summary>Budget reset period. Default: Day.</summary>
    public BudgetPeriod BudgetPeriod { get; set; } = BudgetPeriod.Day;
}

public enum BudgetPeriod { Hour, Day, Week, Month }
