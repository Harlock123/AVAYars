namespace AVAYars.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A moment before the level starts.</summary>
    Start,
    Play,
    /// <summary>Hit: a moment, then the next Yar.</summary>
    Dying,
    /// <summary>The Qotile destroyed: the explosion, then the next level.</summary>
    Destroyed,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Shoot, Hit, Eat, Summon, Cannon, Swirl, Launch, Boom, Die, ExtraLife,
    /// <summary>The loops under play: the Yar's buzz, and the Neutral Zone's hum; and quiet.</summary>
    Buzz, Hum, Quiet,
    TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.8, DyingSeconds = 2.2, BoomSeconds = 3.5, OverSeconds = 5;
    public const int Lives0 = 4, MaxLives = 9;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true;
    private Sfx _loop = Sfx.Quiet;
    private int _bonusSeen;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int Level => World.Level;
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Loop(Sfx.Quiet); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Level);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        _bonusSeen = 0;
        BeginLevel(1);
    }

    private void BeginLevel(int level)
    {
        Sim.StartLevel(World, level);
        SetPhase(Phase.Start);
        Message = level % 2 == 1 ? $"LEVEL {level}: THE ARCH" : $"LEVEL {level}: THE BAND";
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Loop(Sfx.Quiet);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Loop(Sfx.Quiet);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Loop(World.InZone ? Sfx.Hum : Sfx.Buzz);
    }

    /// <summary>The loop under play: the Yar's buzz, the Neutral Zone's hum, or quiet.</summary>
    private void Loop(Sfx s)
    {
        if (s == _loop) return;
        _loop = s;
        Play(s);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
            {
                var swirl = w.Swirl;
                Sim.Step(w, c);
                Loop(w.InZone ? Sfx.Hum : Sfx.Buzz);
                if (w.Bonus > _bonusSeen)
                {
                    _bonusSeen = w.Bonus;
                    Lives = Math.Min(MaxLives, Lives + 1);
                }
                if (w.Destroyed)
                {
                    SetPhase(Phase.Destroyed);
                    Loop(Sfx.Quiet);
                    int points = swirl switch { SwirlState.Home => Sim.QotilePoints, SwirlState.Pad => Sim.PadPoints, _ => Sim.FlightPoints };
                    Message = swirl switch
                    {
                        SwirlState.Home => $"THE QOTILE DESTROYED  +{points}",
                        SwirlState.Pad => $"THE SWIRL ON ITS PAD  +{points}",
                        _ => $"THE SWIRL IN FLIGHT  +{points} AND A YAR",
                    };
                    MessageTime = BoomSeconds;
                }
                else if (w.Dead) { SetPhase(Phase.Dying); Loop(Sfx.Quiet); }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Restart(w);
                    Pilot.Reset();
                    SetPhase(Phase.Start);
                    MessageTime = 0;
                }
                break;
            case Phase.Destroyed:
                // The Yar flies on through the explosion.
                Sim.Step(w, c);
                if (PhaseTime > BoomSeconds) { BeginLevel(w.Level + 1); return; }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Loop(Sfx.Quiet);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a level, past the opening moment.</summary>
    public void Go(int level = 1)
    {
        World = new World();
        Lives = Lives0;
        BeginLevel(level);
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
