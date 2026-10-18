namespace AVAYars.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. It has one plan: chew a tunnel through the shield in
/// front of the Qotile, eating the cells there (which calls up the Zorlon Cannon), then fall back to the left, line the
/// cannon up with where the Qotile will be, and fire, getting out of the way of its own shot. Every few moments it
/// tries each of the nine ways it could fly on a copy of the world for most of a second, with the missile and the
/// Swirl doing what they will, and takes the way that ends nearest its goal without being hit. It fires the cannon
/// only when a copy shows the shot going home. Now and then, with the missile close behind, it rests a moment in the
/// Neutral Zone.
/// </summary>
public sealed class Pilot
{
    public const int Ahead = 40, Every = 2, FireAhead = 150;
    public const double AimX = 300;
    public const int HideTicks = 75, HideEvery = 420;
    private int _wait, _hide, _cool;
    private Controls _last;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _wait = _hide = _cool = 0;
        _last = default;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Dead || w.Destroyed) return default;
        _hide--;
        _cool--;
        // Let go of the button after firing, so the cannon can be fired again.
        if (--_wait > 0) return _last with { Fire = false };
        _wait = Every;
        var (gx, gy) = Goal(w);
        // With the missile close and the Neutral Zone near, a moment's rest in the zone, now and then.
        double zone = (Field.ZoneLeft + Field.ZoneRight) / 2, md = Math.Sqrt((w.Mx - w.X) * (w.Mx - w.X) + (w.My - w.Y) * (w.My - w.Y));
        if (_cool <= 0 && md < 140 && Math.Abs(w.X - zone) < 220 && w.Cannon != CannonState.Ready && w.Swirl != SwirlState.Flying)
        {
            _hide = HideTicks;
            _cool = HideEvery;
        }
        if (_hide > 0) (gx, gy) = (zone, w.Y);
        bool shoot = w.Cannon == CannonState.None && w.X > Field.ZoneRight && Math.Abs(Sim.WrapDy(w.Y, gy)) < 10 && w.Fx > 0 && w.Fy == 0;
        if (w.Cannon == CannonState.Ready && !w.InZone && !w.FireHeld && Math.Abs(Sim.WrapDy(w.Y, gy)) < 80)
            foreach (int dodge in new[] { -1, 1 })
                if (Hits(w, dodge))
                {
                    Doing = "firing";
                    return _last = new Controls(0, dodge, true);
                }
        double best = double.NegativeInfinity;
        var pick = new Controls();
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var c = new Controls(dx, dy, shoot && dx > 0 && dy == 0);
            double v = Value(w, c, gx, gy);
            if (v > best) { best = v; pick = c; }
        }
        Doing = best < -1e5 ? "trapped" : _hide > 0 ? "hiding" : w.Cannon == CannonState.Ready && Tunnel(w) ? "aiming" : "chewing";
        return _last = pick;
    }

    /// <summary>
    /// Where to go: out of the line of the cannon's shot while it's crossing; in front of the next cell of the tunnel to the Qotile (eating it); with the tunnel through and the
    /// cannon called, out to the left in line with where the Qotile will be when the shot gets there; with no cannon,
    /// up to the Qotile itself, to call it.
    /// </summary>
    public static (double X, double Y) Goal(World w)
    {
        double qy = w.QotileY;
        // The shot on its way: out of its line.
        if (w.Cannon == CannonState.Fired) return (AimX, w.Cy + (Sim.WrapDy(w.Cy, w.Y) > 0 ? 70 : -70));
        if (!Tunnel(w))
        {
            for (int c = 0; c < Shield.Cols; c++)
            for (int r = 7; r <= 8; r++)
                if (w.Cells[r, c])
                    return (w.CellLeft(c) - Sim.YarHalf + 4, w.CellTop(r) + Shield.CellH / 2);
        }
        if (w.Cannon == CannonState.Ready)
        {
            double t = (w.QotileX - AimX) / Sim.CannonSpeed;
            return (AimX, QotileLater(w, t));
        }
        if (w.Swirl == SwirlState.Home && w.Cannon == CannonState.None) return (w.QotileX - Shield.QotileHalfW - Sim.YarHalf + 2, qy);
        return (AimX, qy);
    }

    /// <summary>Whether the rows in front of the Qotile are clear right through the shield.</summary>
    public static bool Tunnel(World w)
    {
        for (int c = 0; c < Shield.Cols; c++)
            if (w.Cells[7, c] || w.Cells[8, c]) return false;
        return true;
    }

    /// <summary>Where the Qotile will be after a while, the shield turning back at the top and bottom.</summary>
    private static double QotileLater(World w, double seconds)
    {
        double y = w.ShieldY, vy = w.ShieldVy, lo = Field.Top + 8, hi = Field.Bottom - 8 - Shield.Height;
        for (double t = 0; t < seconds; t += Field.Step)
        {
            y += vy * Field.Step;
            if (y < lo) { y = lo; vy = Math.Abs(vy); }
            if (y > hi) { y = hi; vy = -Math.Abs(vy); }
        }
        return y + Shield.QotileDy;
    }

    /// <summary>Whether firing the cannon now, then flying up (or down) out of its way, destroys the Qotile on a copy.</summary>
    private static bool Hits(World w, int dodge)
    {
        var c = w.Copy();
        Sim.Step(c, new Controls(0, dodge, true));
        if (c.Cannon != CannonState.Fired) return false;
        for (int t = 0; t < FireAhead && !c.Dead && !c.Destroyed && c.Cannon == CannonState.Fired; t++)
            Sim.Step(c, new Controls(0, t < 20 ? dodge : 0));
        return c.Destroyed && !(c.Dead && c.Cause == "the Zorlon Cannon");
    }

    /// <summary>A way tried on a copy, held: how near the goal it ends, and what it scores; heavily marked down if the Yar is hit.</summary>
    private static double Value(World w, Controls first, double gx, double gy)
    {
        var c = w.Copy();
        int score = c.Score;
        bool there = false;
        for (int t = 0; t < Ahead; t++)
        {
            // Stop at the goal rather than fly on past it (but keep watching what comes).
            there |= Math.Abs(c.X - gx) < 4 && Math.Abs(Sim.WrapDy(c.Y, gy)) < 4;
            Sim.Step(c, there ? default : t == 0 ? first : first with { Fire = false });
            if (c.Dead) return -1e6 + t * 1000;
            if (c.Destroyed) return 1e5;
        }
        double dx = c.X - gx, dy = Sim.WrapDy(c.Y, gy);
        // Keep away from the missile when it's close, unless safe in the Neutral Zone.
        double mx = c.Mx - c.X, my = c.My - c.Y, md = Math.Sqrt(mx * mx + my * my);
        double fear = c.InZone ? 0 : Math.Max(0, 90 - md) * 4;
        return -Math.Sqrt(dx * dx + dy * dy) + (c.Score - score) * 0.3 - fear;
    }
}
