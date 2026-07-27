namespace Recite.Core.Model;

public enum MergeEntityKind { Item, Person, Journal, Series }

/// <summary>
/// An audit record of a merge (PLAN §3 "every merge recorded… auditable and reversible").
/// Stores enough to undo: the absorbed entity's full serialized state and the links
/// that were repointed.
/// </summary>
public class MergeLog
{
    public int Id { get; set; }
    public Guid Uuid { get; set; } = Guid.NewGuid();

    public MergeEntityKind Kind { get; set; }

    public Guid SurvivingUuid { get; set; }
    public Guid AbsorbedUuid { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>JSON snapshot of the absorbed entity + repointed link ids, for reversal.</summary>
    public string ProvenanceJson { get; set; } = "{}";

    public bool Reverted { get; set; }
}
