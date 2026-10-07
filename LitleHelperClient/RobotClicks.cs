namespace PixelHelper;

internal sealed class RobotClicks
{
    private DateTime first, blockedUntil;
    private int count;
    internal bool Register(DateTime now, bool dragged)
    {
        if (dragged || now < blockedUntil) { count=0; return false; }
        if (count==0 || now-first>TimeSpan.FromSeconds(1.2) || now<first) { first=now; count=0; }
        if (++count<3) return false;
        count=0; blockedUntil=now.AddSeconds(10); return true;
    }
}
