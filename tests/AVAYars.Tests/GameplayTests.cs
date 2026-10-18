using Avalonia.Input;
using AVAYars.Engine;

namespace AVAYars.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avayars-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A level with the Swirl staying home and the Destroyer Missile far away.</summary>
    private static World Quiet(int level = 1)
    {
        var w = new World();
        Sim.StartLevel(w, level);
        Calm(w);
        return w;
    }

    private static void Calm(World w)
    {
        w.SwirlTime = 1e9;
        w.Mx = -1e6;
        w.My = w.Y;
    }

    private static void Run(World w, double seconds, Controls c = default)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 60); i++) Sim.Step(w, c);
    }

    /// <summary>The shield still: the Qotile at a height that stays put.</summary>
    private static void Hold(World w)
    {
        w.ShieldVy = 0;
    }

    private static void ClearTunnel(World w)
    {
        for (int c = 0; c < Shield.Cols; c++) w.Cells[7, c] = w.Cells[8, c] = false;
    }

    // ───────────────────────────── the shields ─────────────────────────────

    [Fact]
    public void The_arch_and_the_band_take_turns_and_leave_the_Qotile_a_pocket_open_at_the_back()
    {
        Assert.Equal(Shape.Arch, Quiet(1).Shape);
        Assert.Equal(Shape.Band, Quiet(2).Shape);
        Assert.Equal(Shape.Arch, Quiet(3).Shape);
        foreach (var shape in new[] { Shape.Arch, Shape.Band })
        {
            var mask = Shield.Mask(shape);
            Assert.Equal(Shield.Rows, mask.Length);
            Assert.All(mask, row => Assert.Equal(Shield.Cols, row.Length));
            // The Qotile's own cells are empty, and so is everything behind it.
            int qc = (int)(Shield.QotileDx / Shield.CellW);
            for (int r = 7; r <= 8; r++)
            for (int c = qc - 1; c < Shield.Cols; c++)
                Assert.Equal('.', mask[r][c]);
            // But it can't be shot at straight across without breaking through.
            Assert.Equal('#', mask[7][0]);
            Assert.Equal('#', mask[8][0]);
        }
        var w = Quiet(1);
        Assert.Equal(Shield.Count(Shape.Arch), w.CellsLeft);
        // The band's lanes take in each of its cells once.
        var lanes = Shield.Lanes.SelectMany(l => l).ToList();
        Assert.Equal(lanes.Count, lanes.Distinct().Count());
        Assert.Equal(Shield.Count(Shape.Band), lanes.Count);
        Assert.All(lanes, p => Assert.Equal('#', Shield.Band[p.R][p.C]));
    }

    [Fact]
    public void The_shield_goes_up_and_down_with_the_Qotile_and_the_band_runs_round()
    {
        var w = Quiet(1);
        double y0 = w.ShieldY, q0 = w.QotileY;
        Run(w, 1);
        Assert.Equal(y0 + Sim.ShieldSpeed(1), w.ShieldY, 3);
        Assert.Equal(w.ShieldY - y0, w.QotileY - q0, 3);
        // Turning back at the bottom, then at the top.
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < 60 * 30; i++)
        {
            Sim.Step(w, default);
            lo = Math.Min(lo, w.ShieldY);
            hi = Math.Max(hi, w.ShieldY);
            Assert.True(w.ShieldY >= Field.Top && w.ShieldY + Shield.Height <= Field.Bottom);
        }
        Assert.True(hi - lo > 300);
        // The arch keeps its shape; the band carries a gap round with it.
        var arch = Quiet(1);
        var before = (bool[,])arch.Cells.Clone();
        Run(arch, 2);
        Assert.Equal(before, arch.Cells);
        var band = Quiet(2);
        var lane = Shield.Lanes[0];
        band.Cells[lane[0].R, lane[0].C] = false;
        int left = band.CellsLeft;
        int steps = (int)Math.Round(Sim.TurnSeconds(2) * 60);
        Run(band, (steps * 3 + 1) / 60.0);
        Assert.True(band.Cells[lane[0].R, lane[0].C]);
        Assert.False(band.Cells[lane[3].R, lane[3].C]);
        Assert.Equal(left, band.CellsLeft);
    }

    // ───────────────────────────── the Yar ─────────────────────────────

    [Fact]
    public void The_Yar_flies_eight_ways_and_wraps_from_top_to_bottom_but_not_from_side_to_side()
    {
        var w = Quiet();
        double x = w.X, y = w.Y;
        Run(w, 0.5, new Controls(1, 0));
        Assert.Equal(x + Sim.YarSpeed * 0.5, w.X, 3);
        Assert.Equal((1, 0), (w.Fx, w.Fy));
        Run(w, 0.5, new Controls(-1, -1));
        Assert.Equal((-1, -1), (w.Fx, w.Fy));
        Assert.Equal(x + Sim.YarSpeed * 0.5 * (1 - Math.Sqrt(0.5)), w.X, 3);
        Assert.True(w.Y < y);
        // Up off the top, and in again at the bottom.
        w.Y = Field.Top + 10;
        Run(w, 0.2, new Controls(0, -1));
        Assert.True(w.Y > Field.Bottom - 50, $"y {w.Y}");
        // But the left edge stops it.
        Run(w, 3, new Controls(-1, 0));
        Assert.Equal(Field.Left + Sim.YarHalf + 4, w.X, 3);
        // Letting go, it stays where it is, facing the way it went.
        Run(w, 1);
        Assert.Equal((-1, 0), (w.Fx, w.Fy));
        Assert.Equal(Field.Left + Sim.YarHalf + 4, w.X, 3);
    }

    [Fact]
    public void Eating_a_cell_scores_169_pushes_the_Yar_back_and_calls_the_Zorlon_Cannon()
    {
        var w = Quiet();
        Hold(w);
        w.Y = w.QotileY - Shield.CellH / 2;
        w.X = Shield.X - 60;
        Assert.Equal(CannonState.None, w.Cannon);
        int cells = w.CellsLeft;
        double furthest = 0;
        for (int i = 0; i < 30; i++)
        {
            Sim.Step(w, new Controls(1, 0));
            furthest = Math.Max(furthest, w.X);
        }
        Assert.True(w.CellsLeft < cells);
        Assert.Equal(cells - w.CellsLeft, w.Eaten);
        Assert.Equal(w.Eaten * Sim.EatPoints, w.Score);
        Assert.Contains(Sfx.Eat, w.Heard);
        Assert.Contains(Sfx.Summon, w.Heard);
        Assert.Equal(CannonState.Ready, w.Cannon);
        // Pushed back: it never gets far into the shield.
        Assert.True(furthest < Shield.X + 2 * Shield.CellW);
        // A bite at a time, not the whole row at once.
        Assert.True(w.Eaten <= 30 * Field.Step / Sim.EatSeconds + 1);
    }

    [Fact]
    public void Touching_the_Qotile_calls_the_cannon_too()
    {
        var w = Quiet();
        Hold(w);
        ClearTunnel(w);
        w.Y = w.QotileY;
        w.X = w.QotileX - 60;
        Run(w, 0.5, new Controls(1, 0));
        Assert.Equal(CannonState.Ready, w.Cannon);
        Assert.Equal(0, w.Score);
        Assert.False(w.Dead);
        Assert.True(w.X < w.QotileX);
    }

    [Fact]
    public void The_Yar_shoots_cells_for_69_one_shot_at_a_time_and_never_in_the_Neutral_Zone()
    {
        var w = Quiet();
        Hold(w);
        w.Y = w.QotileY - Shield.CellH / 2;
        w.X = 600;
        Sim.Step(w, new Controls(1, 0, true));
        Assert.True(w.ShotOn);
        Assert.Contains(Sfx.Shoot, w.Heard);
        double sx = w.Sx;
        Sim.Step(w, new Controls(0, 0, true));
        Assert.True(w.Sx > sx); // the same shot, gone on
        Run(w, 1);
        Assert.False(w.ShotOn);
        Assert.Equal(1, w.Shot);
        Assert.Equal(Sim.CellPoints, w.Score);
        Assert.False(w.Cells[7, 0]);
        Assert.Equal(CannonState.None, w.Cannon); // shooting doesn't call the cannon
        // In the zone, nothing.
        w.X = (Field.ZoneLeft + Field.ZoneRight) / 2;
        Assert.True(w.InZone);
        Run(w, 0.5, new Controls(0, 0, true));
        Assert.False(w.ShotOn);
        // Shots go the way the Yar faces.
        w.X = 200;
        Sim.Step(w, new Controls(0, 1, true));
        Assert.True(w.ShotOn);
        Assert.Equal((0, Sim.ShotSpeed), (w.Svx, w.Svy));
    }

    // ───────────────────────────── the Zorlon Cannon ─────────────────────────────

    [Fact]
    public void The_cannon_follows_the_Yar_up_and_down_and_a_press_sends_it_straight_across()
    {
        var w = Quiet();
        Hold(w);
        w.Cannon = CannonState.Ready;
        w.Cx = Sim.CannonX;
        w.X = 300;
        w.Y = 150;
        Run(w, 0.5, new Controls(0, 1));
        Assert.Equal(w.Y, w.Cy, 3);
        Assert.Equal(Sim.CannonX, w.Cx);
        // Holding fire from before doesn't fire it: it takes a press.
        w.FireHeld = true;
        Sim.Step(w, new Controls(0, 0, true));
        Assert.Equal(CannonState.Ready, w.Cannon);
        Sim.Step(w, default);
        double y = w.Y;
        Sim.Step(w, new Controls(0, 1, true));
        Assert.Equal(CannonState.Fired, w.Cannon);
        Assert.Contains(Sfx.Cannon, w.Heard);
        Assert.False(w.ShotOn); // the press fired the cannon, not a missile
        // Straight across, whichever way the Yar goes now.
        double cy = w.Cy;
        Assert.True(Math.Abs(cy - y) <= Sim.YarSpeed * Field.Step + 1e-6);
        Run(w, 0.3, new Controls(0, 1));
        Assert.Equal(cy, w.Cy);
        Assert.True(w.Cx > Sim.CannonX + Sim.CannonSpeed * 0.25);
        Assert.False(w.Dead);
    }

    [Fact]
    public void The_cannon_is_stopped_by_a_cell_and_destroys_the_Qotile_through_a_tunnel()
    {
        var w = Quiet();
        Hold(w);
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = w.QotileY;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, -1, true));
        int cells = w.CellsLeft;
        Run(w, 0.15, new Controls(0, -1));
        Run(w, 2);
        Assert.Equal(CannonState.None, w.Cannon);
        Assert.Equal(cells - 1, w.CellsLeft);
        Assert.False(w.Destroyed);
        Assert.Equal(0, w.Score);
        // Through a tunnel: the Qotile is destroyed.
        w = Quiet();
        Hold(w);
        ClearTunnel(w);
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = w.QotileY;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, -1, true));
        Run(w, 0.15, new Controls(0, -1));
        Run(w, 2);
        Assert.True(w.Destroyed);
        Assert.Equal(Sim.QotilePoints, w.Score);
        Assert.Equal(1, w.Qotiles);
        Assert.Contains(Sfx.Boom, w.Heard);
        Assert.Equal((w.QotileX, w.QotileY), (w.BoomX, w.BoomY));
        // In the explosion the Yar still flies, and nothing touches it.
        double x = w.X;
        Run(w, 1, new Controls(1, 0));
        Assert.True(w.X > x);
        Assert.True(w.BoomTime > 0.9);
    }

    [Fact]
    public void The_cannon_can_hit_the_Yar_if_it_stays_in_line()
    {
        var w = Quiet();
        Hold(w);
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = 150;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, 0, true));
        Run(w, 1);
        Assert.True(w.Dead);
        Assert.Equal("the Zorlon Cannon", w.Cause);
        Assert.Contains(Sfx.Die, w.Heard);
    }

    [Fact]
    public void The_Swirl_is_worth_2000_on_its_pad_and_6000_and_a_Yar_in_flight()
    {
        var w = Quiet();
        Hold(w);
        ClearTunnel(w);
        w.Swirl = SwirlState.Pad;
        w.SwirlTime = 1e9;
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = w.QotileY;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, -1, true));
        Run(w, 0.15, new Controls(0, -1));
        Run(w, 2);
        Assert.True(w.Destroyed);
        Assert.Equal(Sim.PadPoints, w.Score);
        Assert.Equal(0, w.Bonus);
        // In flight, in the middle of the screen.
        w = Quiet();
        Hold(w);
        w.Swirl = SwirlState.Flying;
        w.SwX = 700;
        w.SwY = 600;
        w.SwVx = w.SwVy = 0;
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = 600;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, -1, true));
        Run(w, 0.15, new Controls(0, -1));
        Run(w, 1);
        Assert.True(w.Destroyed);
        Assert.Equal(Sim.FlightPoints, w.Score);
        Assert.Equal(1, w.Bonus);
        Assert.Contains(Sfx.ExtraLife, w.Heard);
    }

    // ───────────────────────────── the missile and the Swirl ─────────────────────────────

    [Fact]
    public void The_Destroyer_Missile_comes_after_the_Yar_but_cannot_touch_it_in_the_Neutral_Zone()
    {
        var w = Quiet();
        w.X = 200;
        w.Y = 400;
        w.Mx = 600;
        w.My = 300;
        double d0 = Math.Sqrt((w.Mx - w.X) * (w.Mx - w.X) + (w.My - w.Y) * (w.My - w.Y));
        Run(w, 1);
        double d1 = Math.Sqrt((w.Mx - w.X) * (w.Mx - w.X) + (w.My - w.Y) * (w.My - w.Y));
        Assert.Equal(d0 - Sim.MissileSpeed(1), d1, 3);
        Run(w, 10);
        Assert.True(w.Dead);
        Assert.Equal("the Destroyer Missile", w.Cause);
        // In the zone it goes right through.
        w = Quiet();
        w.X = (Field.ZoneLeft + Field.ZoneRight) / 2;
        w.Y = 400;
        w.Mx = w.X + 100;
        w.My = 400;
        Run(w, 10);
        Assert.False(w.Dead);
        Assert.True(Math.Abs(w.Mx - w.X) < 1);
        // Out of the zone with the missile on it: hit.
        Run(w, 0.3, new Controls(-1, 0));
        Run(w, 1);
        Assert.True(w.Dead);
    }

    [Fact]
    public void The_Destroyer_Missile_does_not_follow_round_the_wrap()
    {
        var w = Quiet();
        w.X = 200;
        w.Y = Field.Top + 20;
        w.Mx = 200;
        w.My = Field.Top + 140;
        Run(w, 0.3, new Controls(0, -1)); // up off the top, to the bottom
        Assert.True(w.Y > Field.Bottom - 80);
        double my = w.My;
        Run(w, 0.5);
        Assert.True(w.My > my); // the long way round, down the screen
        Assert.False(w.Dead);
    }

    [Fact]
    public void The_Qotile_turns_into_the_Swirl_spins_on_its_pad_then_flies_at_the_Yar_even_in_the_zone()
    {
        var w = Quiet();
        Hold(w);
        w.X = (Field.ZoneLeft + Field.ZoneRight) / 2;
        w.Y = 200;
        w.SwirlTime = 0.5;
        Run(w, 0.6);
        Assert.Equal(SwirlState.Pad, w.Swirl);
        Assert.Contains(Sfx.Swirl, w.Heard);
        Run(w, Sim.PadSeconds(1));
        Assert.Equal(SwirlState.Flying, w.Swirl);
        Assert.Contains(Sfx.Launch, w.Heard);
        // Aimed where the Yar was.
        double ang = Math.Atan2(w.SwVy, w.SwVx), want = Math.Atan2(w.Y - w.QotileY, w.X - w.QotileX);
        Assert.Equal(want, ang, 1);
        Assert.Equal(Sim.SwirlSpeed(1), Math.Sqrt(w.SwVx * w.SwVx + w.SwVy * w.SwVy), 3);
        Run(w, 3);
        Assert.True(w.Dead);
        Assert.Equal("the Swirl", w.Cause);
    }

    [Fact]
    public void A_Swirl_that_misses_goes_home_and_from_level_three_it_steers()
    {
        var w = Quiet(1);
        Hold(w);
        w.X = 100;
        w.Y = 200;
        w.Swirl = SwirlState.Flying;
        w.SwX = 900;
        w.SwY = 400;
        w.SwVx = -Sim.SwirlSpeed(1);
        w.SwVy = 0;
        Run(w, 4);
        Assert.False(w.Dead); // straight past, below the Yar
        Assert.Equal(SwirlState.Home, w.Swirl);
        Assert.True(w.SwirlTime > 0);
        // The same on level three: it curves up after the Yar.
        w = Quiet(3);
        Hold(w);
        w.X = 100;
        w.Y = 200;
        w.Swirl = SwirlState.Flying;
        w.SwX = 900;
        w.SwY = 400;
        w.SwVx = -Sim.SwirlSpeed(3);
        w.SwVy = 0;
        Run(w, 0.5);
        Assert.True(w.SwVy < 0);
    }

    [Fact]
    public void Each_level_the_missile_the_Swirl_and_the_shield_are_quicker()
    {
        Assert.True(Sim.MissileSpeed(4) > Sim.MissileSpeed(1));
        Assert.True(Sim.MissileSpeed(20) < Sim.YarSpeed);
        Assert.True(Sim.SwirlSpeed(4) > Sim.SwirlSpeed(1));
        Assert.True(Sim.ShieldSpeed(4) > Sim.ShieldSpeed(1));
        Assert.True(Sim.PadSeconds(5) < Sim.PadSeconds(1));
        Assert.Equal(0, Sim.SwirlTurn(2));
        Assert.True(Sim.SwirlTurn(6) > Sim.SwirlTurn(3));
        Assert.True(Sim.TurnSeconds(6) < Sim.TurnSeconds(2));
    }

    // ───────────────────────────── the computer player ─────────────────────────────

    [Fact]
    public void The_computer_player_destroys_the_Qotile_on_the_first_two_levels()
    {
        var w = new World();
        Sim.StartLevel(w, 1);
        var p = new Pilot();
        int deaths = 0;
        for (int t = 0; t < 60 * 60 * 6 && w.Level <= 2; t++)
        {
            Sim.Step(w, p.Decide(w));
            if (w.Dead) { deaths++; Sim.Restart(w); p.Reset(); }
            if (w.Destroyed) { Sim.StartLevel(w, w.Level + 1); p.Reset(); }
        }
        Assert.Equal(3, w.Level);
        Assert.True(deaths <= 1, $"{deaths} deaths");
    }

    [Fact]
    public void Flying_straight_at_the_shield_firing_gets_hit()
    {
        var w = new World();
        Sim.StartLevel(w, 1);
        int deaths = 0;
        for (int t = 0; t < 60 * 120; t++)
        {
            double dy = Sim.WrapDy(w.Y, w.QotileY);
            Sim.Step(w, new Controls(1, Math.Abs(dy) < 4 ? 0 : Math.Sign(dy), t % 2 == 0));
            if (w.Dead) { deaths++; Sim.Restart(w); }
            if (w.Destroyed) Sim.StartLevel(w, w.Level + 1);
        }
        Assert.True(deaths >= 6, $"{deaths} deaths");
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Four_Yars_an_extra_one_for_the_Swirl_in_flight_then_the_end_and_a_high_score()
    {
        var g = Engine();
        g.Go();
        Assert.Equal(4, g.Lives);
        var w = g.World;
        w.ShieldVy = 0;
        w.Swirl = SwirlState.Flying;
        w.SwX = 700;
        w.SwY = 600;
        w.SwVx = w.SwVy = 0;
        w.Mx = -1e6;
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = 600;
        g.Tick(default);
        g.Tick(new Controls(0, -1, true));
        g.Run(9, new Controls(0, -1));
        g.Run(60);
        Assert.Equal(Phase.Destroyed, g.Phase);
        Assert.Equal(5, g.Lives);
        Assert.Contains("A YAR", g.Message);
        g.Run((int)(60 * GameEngine.BoomSeconds) + 5);
        Assert.Equal(2, g.Level);
        g.SetLives(1);
        g.World.Score += 40000;
        g.World.Mx = g.World.X;
        g.World.My = g.World.Y;
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "YAR") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("YAR", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(Sim.FlightPoints + 40000, g.HighScores.Entries[g.NewEntryIndex].Score);
        Assert.Equal(2, g.HighScores.Entries[g.NewEntryIndex].Stage);
    }

    [Fact]
    public void A_lost_Yar_starts_again_on_the_left_with_the_shield_as_it_was()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        w.Cells[3, 1] = false;
        w.Cannon = CannonState.Ready;
        w.Mx = w.X;
        w.My = w.Y;
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(w.Cells[3, 1]);
        Assert.Equal(Sim.StartX, w.X);
        Assert.Equal(CannonState.None, w.Cannon);
        Assert.Equal(SwirlState.Home, w.Swirl);
    }

    [Fact]
    public void Destroying_the_Qotile_shakes_the_screen_then_the_next_level_has_the_band()
    {
        var g = Engine();
        g.StartGame();
        Assert.Contains("ARCH", g.Message);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        var w = g.World;
        w.ShieldVy = 0;
        w.SwirlTime = 1e9;
        w.Mx = -1e6;
        ClearTunnel(w);
        w.Cannon = CannonState.Ready;
        w.X = 300;
        w.Y = w.QotileY;
        g.Tick(default);
        g.Tick(new Controls(0, 1, true));
        g.Run(9, new Controls(0, 1));
        for (int i = 0; i < 120 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal(Phase.Destroyed, g.Phase);
        Assert.Contains("QOTILE", g.Message);
        Assert.Equal(Sim.QotilePoints, g.Score);
        g.Run((int)(60 * GameEngine.BoomSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Equal(Shape.Band, g.World.Shape);
        Assert.Contains("BAND", g.Message);
        Assert.Equal(Shield.Count(Shape.Band), g.World.CellsLeft);
        Assert.Equal(Sim.QotilePoints, g.Score);
    }

    [Fact]
    public void The_Yars_buzz_turns_to_the_zones_hum_in_the_Neutral_Zone()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go();
        var w = g.World;
        w.SwirlTime = 1e9;
        w.Mx = -1e6;
        g.Tick(default);
        Assert.Equal(Sfx.Buzz, heard.Last(s => s is Sfx.Buzz or Sfx.Hum or Sfx.Quiet));
        w.X = Field.ZoneLeft + 20;
        g.Tick(default);
        Assert.Equal(Sfx.Hum, heard.Last(s => s is Sfx.Buzz or Sfx.Hum or Sfx.Quiet));
        g.Pause();
        Assert.Equal(Sfx.Quiet, heard.Last(s => s is Sfx.Buzz or Sfx.Hum or Sfx.Quiet));
        g.Resume();
        Assert.Equal(Sfx.Hum, heard.Last(s => s is Sfx.Buzz or Sfx.Hum or Sfx.Quiet));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void A_copied_world_goes_on_just_as_the_original()
    {
        var a = new World();
        Sim.StartLevel(a, 2);
        var p = new Pilot();
        for (int i = 0; i < 600; i++) Sim.Step(a, p.Decide(a));
        var b = a.Copy();
        for (int i = 0; i < 600; i++) { Sim.Step(a, new Controls(0, -1, true)); Sim.Step(b, new Controls(0, -1, true)); }
        Assert.Equal(a.Score, b.Score);
        Assert.Equal((a.X, a.Y, a.Mx, a.My), (b.X, b.Y, b.Mx, b.My));
        Assert.Equal((a.Swirl, a.SwX, a.SwY), (b.Swirl, b.SwX, b.SwY));
        Assert.Equal(a.Cells, b.Cells);
        Assert.Equal(a.Dead, b.Dead);
    }

    [Fact]
    public void The_keys_up_flies_up_the_screen_two_arrows_make_a_diagonal_and_space_fires()
    {
        using var input = new InputHandler();
        var w = Quiet();
        double y = w.Y;
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        Run(w, 0.25, input.Player);
        Assert.True(w.Y < y, "up is up the screen");
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        y = w.Y;
        Run(w, 0.25, input.Player);
        Assert.True(w.Y > y, "down is down the screen");
        input.OnKeyDown(Key.Right);
        Assert.Equal(new Controls(1, 1), input.Player);
        // The last of left and right pressed counts.
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        double x = w.X;
        Run(w, 0.25, input.Player);
        Assert.True(w.X > x);
        Assert.True(w.ShotOn);
    }
}
