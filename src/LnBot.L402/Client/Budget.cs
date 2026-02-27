namespace LnBot.L402;

/// <summary>In-memory budget tracker with periodic resets.</summary>
internal class Budget
{
    private readonly L402ClientOptions _options;
    private int _spent;
    private DateTimeOffset _periodStart;
    private readonly object _lock = new();

    public Budget(L402ClientOptions options)
    {
        _options = options;
        _periodStart = DateTimeOffset.UtcNow;
    }

    public void Check(int price)
    {
        lock (_lock)
        {
            ResetIfNewPeriod();
            if (_spent + price > _options.BudgetSats)
                throw new L402BudgetExceededException(
                    $"Payment of {price} sats would exceed budget ({_spent}/{_options.BudgetSats} sats spent)");
        }
    }

    public void Record(int price)
    {
        lock (_lock)
        {
            ResetIfNewPeriod();
            _spent += price;
        }
    }

    private void ResetIfNewPeriod()
    {
        var elapsed = DateTimeOffset.UtcNow - _periodStart;
        var limit = _options.BudgetPeriod switch
        {
            BudgetPeriod.Hour => TimeSpan.FromHours(1),
            BudgetPeriod.Day => TimeSpan.FromDays(1),
            BudgetPeriod.Week => TimeSpan.FromDays(7),
            BudgetPeriod.Month => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(1),
        };

        if (elapsed >= limit)
        {
            _spent = 0;
            _periodStart = DateTimeOffset.UtcNow;
        }
    }
}
