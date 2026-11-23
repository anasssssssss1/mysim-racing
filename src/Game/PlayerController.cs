using Godot;

/// Keyboard / gamepad -> CarInput. Keyboard steering is rate-limited and eased with speed; analog sticks pass through.
public partial class PlayerController : Node
{
    public Car Car;
    float _steer;

    public static void SetupActions()
    {
        void Act(string name, params InputEvent[] events)
        {
            if (InputMap.HasAction(name)) return;
            InputMap.AddAction(name, 0.1f);
            foreach (var e in events) InputMap.ActionAddEvent(name, e);
        }
        InputEventKey Key(Key k) => new() { PhysicalKeycode = k };
        Act("throttle", Key(Key.W), Key(Key.Up), new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1 });
        Act("brake", Key(Key.S), Key(Key.Down), new InputEventJoypadMotion { Axis = JoyAxis.TriggerLeft, AxisValue = 1 });
        Act("steer_left", Key(Key.A), Key(Key.Left), new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 });
        Act("steer_right", Key(Key.D), Key(Key.Right), new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 });
        Act("handbrake", Key(Key.Space), new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Act("reset", Key(Key.R), new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
        Act("camera", Key(Key.C), new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Car == null) return;
        float dt = (float)delta;
        float target = Input.GetAxis("steer_left", "steer_right");
        bool analog = Input.GetJoyAxis(0, JoyAxis.LeftX) != 0;
        float speed = Mathf.Abs(Car.ForwardSpeed);
        float limit = 1f / (1f + speed / 35f); // less lock at speed for digital input
        if (analog) _steer = target;
        else _steer = Mathf.MoveToward(_steer, target * limit, dt * (target == 0 ? 4f : 2.5f));
        Car.In = new CarInput
        {
            Throttle = Input.GetActionStrength("throttle"),
            Brake = Input.GetActionStrength("brake"),
            Steer = _steer,
            Handbrake = Input.IsActionPressed("handbrake"),
        };
    }
}
