using Avalonia.Input;

namespace AVAYars.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: a way to fly, and the fire button.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows (or WASD), any two at once for the diagonals: the last pressed of left and right counts, and of up
    /// and down. Up is -1, as the Yar's y runs down the screen. Space, Z, X or Ctrl to fire. On a gamepad: the stick
    /// or D-pad, and A or B to fire.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = 0, dy = 0;
            foreach (var k in _held)
            {
                switch (k)
                {
                    case Key.Left or Key.A or Key.NumPad4: dx = -1; break;
                    case Key.Right or Key.D or Key.NumPad6: dx = 1; break;
                    case Key.Up or Key.W or Key.NumPad8: dy = -1; break;
                    case Key.Down or Key.S or Key.NumPad2: dy = 1; break;
                }
            }
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.5f ? -1 : pad.DPadRight || pad.LeftStickX > 0.5f ? 1 : 0;
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.5f ? -1 : pad.DPadDown || pad.LeftStickY > 0.5f ? 1 : 0;
            bool fire = Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.ButtonB;
            return new Controls(dx, dy, fire);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
