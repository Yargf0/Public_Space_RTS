using Unity.Entities;
using UnityEngine;

public class MissionDirectorAuthoring : MonoBehaviour
{
    public MissionScript script;

    private sealed class Baker : Baker<MissionDirectorAuthoring>
    {
        public override void Bake(MissionDirectorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new MissionDirectorTag());
            AddComponent(entity, new MissionDirectorState
            {
                missionTime = 0f,
                initialSpawnsDone = false,
            });
            AddBuffer<MissionEventState>(entity);
        }
    }

    public MissionScript GetScript() => script;
}
