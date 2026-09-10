namespace KairoMods.Observer;

public static class MoneyReversal
{
    // 先执行正常扣款，再补回实际扣款的两倍。异常/溢出时不改变正常结果。
    public static long Refund(long before, long after)
    {
        decimal spent = (decimal)before - after;
        decimal refund = spent * 2;
        if (spent <= 0 || refund > long.MaxValue || (decimal)after + refund > long.MaxValue) return 0;
        return (long)refund;
    }
}
