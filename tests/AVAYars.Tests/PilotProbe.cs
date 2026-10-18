using AVAYars.Engine;

namespace AVAYars.Tests;

/// <summary>How the computer player gets on (set YARS_PROBE to run it; YARS_PROBE=lazy just flies at the shield and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("YARS_PROBE") is not { } mode) return;
        int last = int.TryParse(Environment.GetEnvironmentVariable("YARS_LEVELS"), out int n) ? n : 6;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        var p = new Pilot();
        var deaths = new List<string>();
        Sim.StartLevel(w, 1);
        double levelStart = 0;
        int t = 0, fired = 0;
        for (; t < 60 * 60 * 20 && w.Level <= last && deaths.Count < 12; t++)
        {
            Controls c;
            if (mode == "lazy")
            {
                // Straight at the Qotile's height, firing all the way.
                double dy = Sim.WrapDy(w.Y, w.QotileY);
                c = new Controls(1, Math.Abs(dy) < 4 ? 0 : Math.Sign(dy), t % 2 == 0);
            }
            else c = p.Decide(w);
            var cannon = w.Cannon;
            Sim.Step(w, c);
            if (cannon == CannonState.Ready && w.Cannon == CannonState.Fired) fired++;
            if (w.Dead)
            {
                deaths.Add($"L{w.Level} {w.Cause} at ({w.X:0},{w.Y:0}) at {w.Clock - levelStart:0}s, {w.CellsLeft} cells left [{p.Doing}]");
                Sim.Restart(w);
                p.Reset();
            }
            if (w.Destroyed)
            {
                output.WriteLine($"level {w.Level} done in {w.Clock - levelStart:0}s, score {w.Score}, {w.Eaten} eaten, {w.Shot} shot, {fired} fired");
                levelStart = w.Clock;
                double clock = w.Clock;
                int score = w.Score;
                Sim.StartLevel(w, w.Level + 1);
                w.Clock = clock;
                w.Score = score;
                p.Reset();
            }
        }
        output.WriteLine($"reached level {w.Level} after {t / 60}s; {deaths.Count} deaths, score {w.Score}; {sw.ElapsedMilliseconds} ms");
        foreach (var d in deaths) output.WriteLine("   " + d);
    }
}
