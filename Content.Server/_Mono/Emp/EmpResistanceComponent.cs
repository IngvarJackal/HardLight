namespace Content.Server._Mono.Emp;

/// <summary>
/// Reduces EMP energy consumption applied to this entity. The EMP system
/// multiplies its energy-consumption value by <see cref="Coefficient"/>; a
/// coefficient of 0 means the entity is fully immune.
/// </summary>
[RegisterComponent]
public sealed partial class EmpResistanceComponent : Component
{
    [DataField]
    public float Coefficient = 1f;
}
