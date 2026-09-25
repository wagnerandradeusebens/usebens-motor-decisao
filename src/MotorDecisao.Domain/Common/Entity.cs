namespace MotorDecisao.Domain.Common;

/// <summary>
/// Base class for all persistent entities. Provides a surrogate key and
/// audit timestamps that are common to every table in the engine.
/// </summary>
public abstract class Entity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp of when the record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update, if any.</summary>
    public DateTime? UpdatedAt { get; set; }
}
