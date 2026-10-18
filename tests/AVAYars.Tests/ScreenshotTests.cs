using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAYars.Engine;
using AVAYars.Views;
using Controls = AVAYars.Engine.Controls;

namespace AVAYars.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set YARS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avayars-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("YARS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAYars-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 48269, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Chewing_the_shield() => Moment(1, "play", g => g.World.Level == 1 && g.World.Eaten >= 2 && g.World.X > 700 && g.World.Swirl == SwirlState.Home);

    [AvaloniaFact]
    public void The_Neutral_Zone() =>
        Moment(1, "neutral-zone", g => g.World.InZone && g.Level <= 4 && Math.Abs(g.World.X - (Field.ZoneLeft + Field.ZoneRight) / 2) < 24
                                       && Math.Sqrt(Math.Pow(g.World.Mx - g.World.X, 2) + Math.Pow(g.World.My - g.World.Y, 2)) < 110);

    [AvaloniaFact]
    public void The_Zorlon_Cannon() => Moment(1, "zorlon-cannon", g => g.World.Cannon == CannonState.Fired && g.World.Cx is > 560 and < 640);

    [AvaloniaFact]
    public void The_Swirl() => Moment(3, "swirl", g => g.World.Swirl == SwirlState.Flying && g.World.SwX is > 520 and < 720 && g.World.SwY is > Field.Top + 40 and < Field.Bottom - 40
                                                     && Math.Abs(g.World.SwX - g.World.X) + Math.Abs(g.World.SwY - g.World.Y) > 150);

    [AvaloniaFact]
    public void The_Qotile_destroyed() => Moment(1, "qotile-destroyed", g => g.World.Destroyed && g.World.BoomTime is > 0.35 and < 0.45);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(1);
        g.SetLives(1);
        g.World.Score = 3245;
        // Sit still out on the left: the missile gets you.
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++)
        {
            g.Tick(default);
            if (i % 3 == 0) c.Advance(0.05);
        }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
