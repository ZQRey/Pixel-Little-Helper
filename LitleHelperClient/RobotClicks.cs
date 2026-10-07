namespace PixelHelper;
internal enum RobotClickReaction { None, Greeting, Offended }

internal sealed class RobotClicks
{
    private DateTime first, blockedUntil;
    private int count;
    internal RobotClickReaction Register(DateTime now, bool dragged)
    {
        if (dragged || now < blockedUntil) { count=0; return RobotClickReaction.None; }
        if (count==0 || now-first>TimeSpan.FromSeconds(1.2) || now<first) { first=now; count=0; }
        count++;
        if (count==3) return RobotClickReaction.Greeting;
        if (count<5) return RobotClickReaction.None;
        count=0; blockedUntil=now.AddSeconds(10); return RobotClickReaction.Offended;
    }
}
