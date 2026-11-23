using Godot;

/// One car's numbers. Defaults: a ~1300 kg, ~480 hp rear-drive GT car.
public sealed class CarSpec
{
    public float Mass = 1300f;
    public Vector3 Inertia = new(2000f, 2400f, 550f); // pitch, yaw, roll (kg m^2)
    public float CgHeight = 0.11f;    // above hub height (hubs sit 0.34 m off the ground) -> 0.45 m
    public float CgZ = 0.08f;         // + = behind the middle (rear weight bias)

    // geometry (body frame: -Z forward, +X right, +Y up; origin at hub height)
    public float Wheelbase = 2.7f;
    public float TrackFront = 1.65f, TrackRear = 1.62f;
    public float WheelRadius = 0.34f;
    public float WheelInertia = 1.2f;  // kg m^2 per wheel incl. brake disc
    public float MountHeight = 0.12f;

    // suspension
    public float RestLength = 0.15f;
    public float SpringFront = 90000f, SpringRear = 100000f;    // N/m (wheel rate)
    public float BumpFront = 3500f, BumpRear = 3800f;           // N s/m
    public float ReboundFront = 6000f, ReboundRear = 6500f;
    public float ArbFront = 40000f, ArbRear = 30000f;            // N/m of left-right compression difference
    public float BumpStop = 400000f;

    // steering
    public float MaxSteer = 0.42f;    // rad at the wheel (~24 deg)

    // engine: rpm -> Nm
    public float[] TorqueRpm = { 1000, 3000, 5000, 6500, 7500, 8500, 9000 };
    public float[] TorqueNm = { 300, 420, 470, 480, 460, 420, 380 };
    public float IdleRpm = 1000f, Redline = 9000f;
    public float UpshiftRpm = 8600f, DownshiftRpm = 5800f;
    public float[] Gears = { 3.0f, 2.3f, 1.85f, 1.55f, 1.35f, 1.2f };
    public float ReverseGear = 3.0f;
    public float FinalDrive = 3.4f;
    public float Efficiency = 0.9f;
    public float ShiftTime = 0.08f;
    public float LsdPreload = 150f;   // Nm
    public float LsdRamp = 0.4f;      // extra locking per Nm of drive torque

    // brakes
    public float BrakeTorque = 8000f; // Nm total
    public float BrakeBias = 0.6f;    // front share
    public float HandbrakeTorque = 3000f;
    public bool Abs = true;
    public bool TractionControl = true;

    // aero (rho = 1.2)
    public float CdA = 0.75f;
    public float ClA = 2.0f;          // downforce
    public float AeroBalance = 0.45f; // front share

    public float RollingResistance = 0.012f;

    public TyreModel Tyre = new();

    public float EngineTorque(float rpm)
    {
        if (rpm <= TorqueRpm[0]) return TorqueNm[0];
        for (int i = 1; i < TorqueRpm.Length; i++)
            if (rpm <= TorqueRpm[i])
                return Mathf.Lerp(TorqueNm[i - 1], TorqueNm[i], (rpm - TorqueRpm[i - 1]) / (TorqueRpm[i] - TorqueRpm[i - 1]));
        return TorqueNm[^1];
    }

    public float EngineDrag(float rpm) => 30f + 0.006f * rpm; // Nm of engine braking off-throttle
}
