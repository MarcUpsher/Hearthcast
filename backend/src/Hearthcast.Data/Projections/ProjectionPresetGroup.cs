namespace Hearthcast.Data.Projections;

public sealed class ProjectionPresetGroup
{
    private ProjectionPresetGroup() { }
    internal ProjectionPresetGroup(ProjectionPreset preset, ProjectionGroupSelection selection)
    {
        ProjectionPresetId = preset.Id;
        HouseholdId = preset.HouseholdId;
        GroupId = selection.GroupId;
        Bucket = selection.Bucket;
    }
    internal void SetBucket(ProjectionBucket bucket) => Bucket = bucket;
    public Guid ProjectionPresetId { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid GroupId { get; private set; }
    public ProjectionBucket Bucket { get; private set; }
}
