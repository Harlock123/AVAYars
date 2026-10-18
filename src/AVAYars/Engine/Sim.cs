namespace AVAYars.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double YarSpeed = 240, ShotSpeed = 720, CannonSpeed = 640;
    /// <summary>Half the size of each thing, for touching.</summary>
    public const double YarHalf = 14, ShotHalf = 4, MissileHalf = 4, CannonHalfW = 12, CannonHalfH = 6, SwirlHalf = 18;
    public const double CannonX = 24, StartX = 96, EatSeconds = 0.12, Push = 10;
    public const int CellPoints = 69, EatPoints = 169, QotilePoints = 1000, PadPoints = 2000, FlightPoints = 6000;

    public static double MissileSpeed(int level) => Math.Min(150, 66 + (level - 1) * 9);
    public static double ShieldSpeed(int level) => Math.Min(100, 46 + (level - 1) * 6);
    public static double SwirlSpeed(int level) => Math.Min(420, 280 + (level - 1) * 20);
    /// <summary>How long the Swirl spins on its pad before it goes.</summary>
    public static double PadSeconds(int level) => Math.Max(0.6, 1.4 - (level - 1) * 0.1);
    /// <summary>How long the Qotile waits before turning into the Swirl: less each level.</summary>
    public static double SwirlWait(World w) => Math.Max(2, 6 - (w.Level - 1) * 0.4) + w.Dice.Next() * 3;
    /// <summary>From the third level the Swirl steers after the Yar in flight (radians a second).</summary>
    public static double SwirlTurn(int level) => level < 3 ? 0 : Math.Min(1.6, 0.8 + (level - 3) * 0.15);
    /// <summary>How often the band steps its cells round.</summary>
    public static double TurnSeconds(int level) => Math.Max(0.06, 0.14 - (level - 1) * 0.01);

    public static void StartLevel(World w, int level)
    {
        w.Level = level;
        w.Shape = level % 2 == 1 ? Shape.Arch : Shape.Band;
        var mask = Shield.Mask(w.Shape);
        for (int r = 0; r < Shield.Rows; r++)
        for (int c = 0; c < Shield.Cols; c++)
            w.Cells[r, c] = mask[r][c] == '#';
        w.ShieldY = Field.Top + (Field.PlayHeight - Shield.Height) / 2;
        w.ShieldVy = ShieldSpeed(level);
        w.Turn = 0;
        w.Destroyed = false;
        w.BoomTime = 0;
        w.Dice = new Dice(level * 31 + 7);
        Restart(w);
    }

    /// <summary>A fresh Yar on the left, the Qotile at home, the missile at the shield; the shield as it was.</summary>
    public static void Restart(World w)
    {
        w.Dead = false;
        w.Cause = "";
        w.X = StartX;
        w.Y = Field.Top + Field.PlayHeight / 2;
        w.Fx = 1;
        w.Fy = 0;
        w.Moving = false;
        w.ShotOn = false;
        w.Cannon = CannonState.None;
        w.Mx = Shield.X - 40;
        w.My = w.QotileY;
        w.Swirl = SwirlState.Home;
        w.SwirlTime = SwirlWait(w);
        w.EatWait = 0;
        w.FireHeld = true;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        if (w.Dead) return;
        w.Clock += dt;
        MoveYar(w, c, dt);
        bool press = c.Fire && !w.FireHeld;
        w.FireHeld = c.Fire;
        if (w.Destroyed)
        {
            w.BoomTime += dt;
            return;
        }
        Fire(w, c.Fire, press);
        MoveShield(w, dt);
        Nibble(w, dt);
        MoveShot(w, dt);
        MoveCannon(w, dt);
        if (w.Destroyed) return;
        MoveSwirl(w, dt);
        MoveMissile(w, dt);
        Touches(w);
    }

    // ───────────────────────────── the Yar ─────────────────────────────

    public static double Wrap(double y) => y < Field.Top ? y + Field.PlayHeight : y >= Field.Bottom ? y - Field.PlayHeight : y;

    /// <summary>The shortest way from one height to another, round the wrap or not.</summary>
    public static double WrapDy(double from, double to)
    {
        double d = to - from;
        if (d > Field.PlayHeight / 2) d -= Field.PlayHeight;
        if (d < -Field.PlayHeight / 2) d += Field.PlayHeight;
        return d;
    }

    private static void MoveYar(World w, Controls c, double dt)
    {
        int dx = Math.Sign(c.Dx), dy = Math.Sign(c.Dy);
        w.Moving = dx != 0 || dy != 0;
        if (!w.Moving) return;
        w.Fx = dx;
        w.Fy = dy;
        double k = dx != 0 && dy != 0 ? Math.Sqrt(0.5) : 1;
        w.X = Math.Clamp(w.X + dx * YarSpeed * k * dt, Field.Left + YarHalf + 4, Field.Right - YarHalf - 4);
        w.Y = Wrap(w.Y + dy * YarSpeed * k * dt);
    }

    private static bool Overlap(double ax, double ay, double ahw, double ahh, double bx, double by, double bhw, double bhh) =>
        Math.Abs(ax - bx) < ahw + bhw && Math.Abs(ay - by) < ahh + bhh;

    private static bool TouchesYar(World w, double x, double y, double hw, double hh) =>
        Overlap(w.X, w.Y, YarHalf, YarHalf, x, y, hw, hh) || Overlap(w.X, w.Y + Field.PlayHeight, YarHalf, YarHalf, x, y, hw, hh)
        || Overlap(w.X, w.Y - Field.PlayHeight, YarHalf, YarHalf, x, y, hw, hh);

    /// <summary>The first cell still there that a box touches (leftmost, then topmost), or (-1, -1).</summary>
    public static (int R, int C) CellAt(World w, double x, double y, double hw, double hh)
    {
        int c0 = Math.Max(0, (int)Math.Floor((x - hw - Shield.X) / Shield.CellW)), c1 = Math.Min(Shield.Cols - 1, (int)Math.Floor((x + hw - Shield.X) / Shield.CellW));
        int r0 = Math.Max(0, (int)Math.Floor((y - hh - w.ShieldY) / Shield.CellH)), r1 = Math.Min(Shield.Rows - 1, (int)Math.Floor((y + hh - w.ShieldY) / Shield.CellH));
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
        {
            if (!w.Cells[r, c]) continue;
            // Cells only touch where they really overlap (the edges of the range are inclusive).
            double cx = w.CellLeft(c) + Shield.CellW / 2, cy = w.CellTop(r) + Shield.CellH / 2;
            if (Overlap(x, y, hw, hh, cx, cy, Shield.CellW / 2, Shield.CellH / 2)) return (r, c);
        }
        return (-1, -1);
    }

    private static void Summon(World w)
    {
        if (w.Cannon != CannonState.None) return;
        w.Cannon = CannonState.Ready;
        w.Cx = CannonX;
        w.Cy = w.Y;
        w.Heard.Add(Sfx.Summon);
    }

    /// <summary>The Yar against the shield: it takes a bite of a cell it touches and is pushed back. Touching the Qotile calls the cannon too.</summary>
    private static void Nibble(World w, double dt)
    {
        w.EatWait = Math.Max(0, w.EatWait - dt);
        var (r, c) = CellAt(w, w.X, w.Y, YarHalf, YarHalf);
        if (r >= 0)
        {
            if (w.EatWait <= 0)
            {
                w.Cells[r, c] = false;
                w.Score += EatPoints;
                w.Eaten++;
                w.EatWait = EatSeconds;
                w.Heard.Add(Sfx.Eat);
                Summon(w);
            }
            w.X -= Push;
            return;
        }
        if (w.Swirl == SwirlState.Home && Overlap(w.X, w.Y, YarHalf, YarHalf, w.QotileX, w.QotileY, Shield.QotileHalfW, Shield.QotileHalfH))
        {
            w.X -= Push;
            Summon(w);
        }
    }

    // ───────────────────────────── firing ─────────────────────────────

    /// <summary>
    /// No firing in the Neutral Zone. With the Zorlon Cannon there, a press of the button fires it; otherwise the Yar
    /// fires an energy missile the way it faces, one at a time, for as long as the button is held.
    /// </summary>
    private static void Fire(World w, bool held, bool press)
    {
        if (w.InZone) return;
        if (w.Cannon == CannonState.Ready)
        {
            if (!press) return;
            w.Cannon = CannonState.Fired;
            w.Cy = w.Y;
            w.Heard.Add(Sfx.Cannon);
            return;
        }
        if (!held || w.ShotOn) return;
        w.ShotOn = true;
        w.Sx = w.X + w.Fx * YarHalf;
        w.Sy = w.Y + w.Fy * YarHalf;
        double k = w.Fx != 0 && w.Fy != 0 ? Math.Sqrt(0.5) : 1;
        w.Svx = w.Fx * ShotSpeed * k;
        w.Svy = w.Fy * ShotSpeed * k;
        w.Heard.Add(Sfx.Shoot);
    }

    private static void MoveShot(World w, double dt)
    {
        if (!w.ShotOn) return;
        w.Sx += w.Svx * dt;
        w.Sy += w.Svy * dt;
        if (w.Sx < Field.Left || w.Sx > Field.Right || w.Sy < Field.Top || w.Sy > Field.Bottom) { w.ShotOn = false; return; }
        var (r, c) = CellAt(w, w.Sx, w.Sy, ShotHalf, ShotHalf);
        if (r < 0) return;
        w.Cells[r, c] = false;
        w.ShotOn = false;
        w.Score += CellPoints;
        w.Shot++;
        w.Heard.Add(Sfx.Hit);
    }

    /// <summary>The cannon follows the Yar up and down until fired; then its shot goes straight across, and whatever it meets first is done for.</summary>
    private static void MoveCannon(World w, double dt)
    {
        if (w.Cannon == CannonState.Ready) { w.Cy = w.Y; return; }
        if (w.Cannon != CannonState.Fired) return;
        w.Cx += CannonSpeed * dt;
        if (w.Cx - CannonHalfW > Field.Right) { w.Cannon = CannonState.None; return; }
        if (TouchesYar(w, w.Cx, w.Cy, CannonHalfW, CannonHalfH))
        {
            w.Cannon = CannonState.None;
            Die(w, "the Zorlon Cannon");
            return;
        }
        var (r, c) = CellAt(w, w.Cx, w.Cy, CannonHalfW, CannonHalfH);
        if (r >= 0)
        {
            // A cell takes the blast.
            w.Cells[r, c] = false;
            w.Cannon = CannonState.None;
            w.Heard.Add(Sfx.Hit);
            return;
        }
        bool home = w.Swirl != SwirlState.Flying;
        double tx = home ? w.QotileX : w.SwX, ty = home ? w.QotileY : w.SwY;
        double hw = home ? Shield.QotileHalfW : SwirlHalf, hh = home ? Shield.QotileHalfH : SwirlHalf;
        if (!Overlap(w.Cx, w.Cy, CannonHalfW, CannonHalfH, tx, ty, hw, hh)) return;
        int points = w.Swirl switch { SwirlState.Home => QotilePoints, SwirlState.Pad => PadPoints, _ => FlightPoints };
        w.Score += points;
        if (w.Swirl == SwirlState.Flying)
        {
            w.Bonus++;
            w.Heard.Add(Sfx.ExtraLife);
        }
        w.Qotiles++;
        w.Destroyed = true;
        w.BoomTime = 0;
        w.BoomX = tx;
        w.BoomY = ty;
        w.Cannon = CannonState.None;
        w.ShotOn = false;
        w.Heard.Add(Sfx.Boom);
    }

    // ───────────────────────────── the shield, the Swirl and the missile ─────────────────────────────

    /// <summary>Up and down, turning back at the top and the bottom; the band's cells stepping round as it goes.</summary>
    private static void MoveShield(World w, double dt)
    {
        w.ShieldY += w.ShieldVy * dt;
        double lo = Field.Top + 8, hi = Field.Bottom - 8 - Shield.Height;
        if (w.ShieldY < lo) { w.ShieldY = lo; w.ShieldVy = Math.Abs(w.ShieldVy); }
        if (w.ShieldY > hi) { w.ShieldY = hi; w.ShieldVy = -Math.Abs(w.ShieldVy); }
        if (w.Shape != Shape.Band) return;
        w.Turn += dt;
        if (w.Turn < TurnSeconds(w.Level)) return;
        w.Turn -= TurnSeconds(w.Level);
        foreach (var lane in Shield.Lanes)
        {
            bool last = w.Cells[lane[^1].R, lane[^1].C];
            for (int i = lane.Length - 1; i > 0; i--) w.Cells[lane[i].R, lane[i].C] = w.Cells[lane[i - 1].R, lane[i - 1].C];
            w.Cells[lane[0].R, lane[0].C] = last;
        }
    }

    /// <summary>The Qotile, now and then, turns into the Swirl, spins on its pad, then flies at the Yar.</summary>
    private static void MoveSwirl(World w, double dt)
    {
        switch (w.Swirl)
        {
            case SwirlState.Home:
                if ((w.SwirlTime -= dt) > 0) return;
                w.Swirl = SwirlState.Pad;
                w.SwirlTime = PadSeconds(w.Level);
                w.Heard.Add(Sfx.Swirl);
                return;
            case SwirlState.Pad:
                if ((w.SwirlTime -= dt) > 0) return;
                w.Swirl = SwirlState.Flying;
                w.SwX = w.QotileX;
                w.SwY = w.QotileY;
                double dx = w.X - w.SwX, dy = w.Y - w.SwY, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                w.SwVx = dx / d * SwirlSpeed(w.Level);
                w.SwVy = dy / d * SwirlSpeed(w.Level);
                w.Heard.Add(Sfx.Launch);
                return;
            case SwirlState.Flying:
                if (SwirlTurn(w.Level) is > 0 and var turn)
                {
                    double heading = Math.Atan2(w.SwVy, w.SwVx), want = Math.Atan2(w.Y - w.SwY, w.X - w.SwX);
                    double diff = Math.IEEERemainder(want - heading, 2 * Math.PI);
                    heading += Math.Clamp(diff, -turn * dt, turn * dt);
                    double v = SwirlSpeed(w.Level);
                    w.SwVx = Math.Cos(heading) * v;
                    w.SwVy = Math.Sin(heading) * v;
                }
                w.SwX += w.SwVx * dt;
                w.SwY += w.SwVy * dt;
                if (w.SwX < Field.Left - 40 || w.SwX > Field.Right + 40 || w.SwY < Field.Top - 40 || w.SwY > Field.Bottom + 40)
                {
                    // Gone off the screen: the Qotile is back on its pad.
                    w.Swirl = SwirlState.Home;
                    w.SwirlTime = SwirlWait(w);
                }
                return;
        }
    }

    /// <summary>Straight at the Yar, slowly, never round the wrap.</summary>
    private static void MoveMissile(World w, double dt)
    {
        double dx = w.X - w.Mx, dy = w.Y - w.My, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-6) return;
        double go = Math.Min(d, MissileSpeed(w.Level) * dt);
        w.Mx += dx / d * go;
        w.My += dy / d * go;
    }

    private static void Touches(World w)
    {
        if (!w.InZone && TouchesYar(w, w.Mx, w.My, MissileHalf, MissileHalf)) { Die(w, "the Destroyer Missile"); return; }
        if (w.Swirl == SwirlState.Pad && TouchesYar(w, w.QotileX, w.QotileY, SwirlHalf, SwirlHalf)
            || w.Swirl == SwirlState.Flying && TouchesYar(w, w.SwX, w.SwY, SwirlHalf, SwirlHalf))
            Die(w, "the Swirl");
    }

    private static void Die(World w, string cause)
    {
        w.Dead = true;
        w.Cause = cause;
        w.ShotOn = false;
        w.Heard.Add(Sfx.Die);
    }
}
