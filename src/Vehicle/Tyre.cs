using Godot;

/// Pacejka "Magic Formula" tyre with load-sensitive grip and combined slip by normalised slip vector.
/// Slip ratio kappa: + when the wheel spins faster than the road (drive). Slip angle alpha: + gives + lateral force.
public sealed class TyreModel
{
    public float Mu = 1.6f;          // peak friction at nominal load
    public float Fz0 = 3200f;        // nominal load, N
    public float LoadSens = 0.12f;   // grip lost per unit of extra relative load
    public float Bx = 12f, Cx = 1.65f, Ex = 0.3f;
    public float By = 10f, Cy = 1.9f, Ey = 0.5f;
    public readonly float KappaPeak, AlphaPeak;

    public TyreModel()
    {
        KappaPeak = Peak(Bx, Cx, Ex);
        AlphaPeak = Peak(By, Cy, Ey);
    }

    public static float Shape(float x, float b, float c, float e)
    {
        float bx = b * x;
        return Mathf.Sin(c * Mathf.Atan(bx - e * (bx - Mathf.Atan(bx))));
    }

    static float Peak(float b, float c, float e)
    {
        float best = 0, at = 0;
        for (float s = 0.0005f; s < 0.8f; s += 0.0005f)
        {
            float f = Shape(s, b, c, e);
            if (f > best) { best = f; at = s; }
        }
        return at;
    }

    public float MuAt(float fz) => Mu * Mathf.Max(0.3f, 1f - LoadSens * (fz / Fz0 - 1f));

    /// (Fx, Fy) in N.
    public Vector2 Force(float kappa, float alpha, float fz)
    {
        if (fz <= 0) return Vector2.Zero;
        float sx = kappa / KappaPeak, sy = alpha / AlphaPeak;
        float rho = Mathf.Sqrt(sx * sx + sy * sy);
        if (rho < 1e-6f) return Vector2.Zero;
        float d = MuAt(fz) * fz;
        return new Vector2(
            d * Shape(rho * KappaPeak, Bx, Cx, Ex) * sx / rho,
            d * Shape(rho * AlphaPeak, By, Cy, Ey) * sy / rho);
    }
}
