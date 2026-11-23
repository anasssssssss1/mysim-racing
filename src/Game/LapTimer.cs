using Godot;

/// Lap timing by progress along the centreline: a lap counts when the car crosses the start
/// having passed the halfway point, so cutting back across the line does not count.
public partial class LapTimer : Node
{
    public Car Car;
    public TrackBuilder Track;
    public int Lap = 1;
    public float Current, Best = -1;
    int _index;
    bool _half;

    public override void _PhysicsProcess(double delta)
    {
        if (Car == null) return;
        Current += (float)delta;
        int n = Track.Centre.Length;
        int i = Track.Nearest(Car.GlobalPosition, _index, 60);
        if (i > n / 3 && i < 2 * n / 3) _half = true;
        if (_half && _index > n - 50 && i < 50)
        {
            if (Best < 0 || Current < Best) Best = Current;
            Current = 0; Lap++; _half = false;
        }
        _index = i;
    }

    public static string Fmt(float t) => t < 0 ? "--:--.---" : $"{(int)(t / 60)}:{t % 60:00.000}";
}
