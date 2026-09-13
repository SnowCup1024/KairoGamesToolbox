using KairoMods.Protocol;

namespace KairoMods.DoraemonDorayakiShopStory;

public static class MoneyReversal
{
    // 按实际扣款与统一倍率补回；0 倍不消耗。异常/溢出时不改变正常结果。
    public static long Refund(long before, long after, int multiplier = 1, long maximum = long.MaxValue)
    {
        decimal spent = (decimal)before - after;
        decimal refund = spent * (multiplier + 1m);
        if (!ControlProtocol.ValidMultiplier(multiplier) || spent <= 0 || refund > maximum || (decimal)after + refund > maximum) return 0;
        return (long)refund;
    }
}
