using Unity.Entities;

public static class SquadFormationRuntimeUtility
{
    public static int RebuildCompactFormationSlots(EntityManager em, Entity squadEntity)
    {
        if (squadEntity == Entity.Null ||
            !em.Exists(squadEntity) ||
            !em.HasBuffer<SquadMember>(squadEntity))
        {
            return 0;
        }

        DynamicBuffer<SquadMember> members = em.GetBuffer<SquadMember>(squadEntity);
        return RebuildCompactFormationSlots(em, members);
    }

    public static int RebuildCompactFormationSlots(EntityManager em, DynamicBuffer<SquadMember> members)
    {
        SortMembersByStableSlot(members);

        for (int i = 0; i < members.Length; i++)
        {
            SquadMember member = members[i];
            member.formationSlotIndex = i;
            members[i] = member;

            if (member.ship == Entity.Null ||
                !em.Exists(member.ship) ||
                !em.HasComponent<ShipSquadRef>(member.ship))
            {
                continue;
            }

            ShipSquadRef squadRef = em.GetComponentData<ShipSquadRef>(member.ship);
            squadRef.slotIndex = member.slotIndex;
            squadRef.formationSlotIndex = member.formationSlotIndex;
            em.SetComponentData(member.ship, squadRef);
        }

        return members.Length;
    }

    private static void SortMembersByStableSlot(DynamicBuffer<SquadMember> members)
    {
        for (int i = 1; i < members.Length; i++)
        {
            SquadMember current = members[i];
            int j = i - 1;

            while (j >= 0 && members[j].slotIndex > current.slotIndex)
            {
                members[j + 1] = members[j];
                j--;
            }

            members[j + 1] = current;
        }
    }
}
