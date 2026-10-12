namespace Ada.Core;

/// <summary>Kritik altı sönümlü yay: hafif "overshoot" ile premium his.</summary>
public struct Spring
{
    public float Value, Velocity, Target;
    public float Stiffness, Damping;

    public Spring(float value, float stiffness = 320f, float damping = 26f)
    {
        Value = Target = value; Velocity = 0; Stiffness = stiffness; Damping = damping;
    }

    public bool Settled => MathF.Abs(Value - Target) < 0.01f && MathF.Abs(Velocity) < 0.01f;

    public void Step(float dt)
    {
        if (Settled) { Value = Target; Velocity = 0; return; }
        const float h = 1f / 240f; // sabit alt adım = kararlı entegrasyon
        while (dt > 0)
        {
            float s = MathF.Min(dt, h);
            float a = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += a * s;
            Value += Velocity * s;
            dt -= s;
        }
        if (Settled) { Value = Target; Velocity = 0; }
    }

    public void Snap(float v) { Value = Target = v; Velocity = 0; }
}

/// <summary>0..1 arası üstel yumuşatma (hover, fade vb.).</summary>
public struct Smooth
{
    public float Value, Target;
    public float Speed;
    public Smooth(float v, float speed = 14f) { Value = Target = v; Speed = speed; }
    public bool Settled => MathF.Abs(Value - Target) < 0.002f;
    public void Step(float dt)
    {
        Value += (Target - Value) * (1f - MathF.Exp(-Speed * dt));
        if (Settled) Value = Target;
    }
}

public static class Ease
{
    public static float Clamp01(float t) => t < 0 ? 0 : t > 1 ? 1 : t;
    public static float OutCubic(float t) { t = Clamp01(t); var u = 1 - t; return 1 - u * u * u; }
    public static float InOutCubic(float t) { t = Clamp01(t); return t < .5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2; }
    public static float SmoothStep(float a, float b, float x) { var t = Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
