namespace KairoMods.Observer;

public static class MoneyReversal
{
    // 先执行正常扣款，再补回实际扣款的两倍。异常/溢出时不改变正常结果。
    public static long Refund(long before, long after, int multiplier = 1, long maximum = long.MaxValue)
    {
        decimal spent = (decimal)before - after;
        decimal refund = spent * (multiplier + 1m);
        if (multiplier is not (1 or 2 or 5 or 20) || spent <= 0 || refund > maximum || (decimal)after + refund > maximum) return 0;
        return (long)refund;
    }
}
