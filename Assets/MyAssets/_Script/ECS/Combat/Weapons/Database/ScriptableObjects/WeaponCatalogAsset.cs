using System.Collections.Generic;
using UnityEngine;

// weapon catalog. array of WeaponProfileSO refs
// index in array = profileId, no manual ids
[CreateAssetMenu(fileName = "WeaponCatalog", menuName = "Weapon/Weapon Catalog")]
public class WeaponCatalogAsset : ScriptableObject
{
    [Header("Weapon profiles. Index = profileId. Order matters, don't reorder")]
    public WeaponProfileSO[] profiles;

    private Dictionary<WeaponProfileSO, int> idByProfile;

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();

        // validation: duplicates and nulls
        if (profiles == null)
            return;

        HashSet<WeaponProfileSO> seen = new HashSet<WeaponProfileSO>();
        for (int i = 0; i < profiles.Length; i++)
        {
            WeaponProfileSO p = profiles[i];
            if (p == null)
            {
                Debug.LogWarning($"WeaponCatalogAsset '{name}': slot {i} is empty.", this);
                continue;
            }
            if (!seen.Add(p))
                Debug.LogError($"WeaponCatalogAsset '{name}': duplicate '{p.name}' in slot {i}.", this);
        }
    }

    private void RebuildCache()
    {
        idByProfile = new Dictionary<WeaponProfileSO, int>();
        if (profiles == null) return;
        for (int i = 0; i < profiles.Length; i++)
        {
            if (profiles[i] != null && !idByProfile.ContainsKey(profiles[i]))
                idByProfile[profiles[i]] = i;
        }
    }

    public bool TryGetProfileId(WeaponProfileSO profile, out int id)
    {
        if (idByProfile == null)
            RebuildCache();

        return idByProfile.TryGetValue(profile, out id);
    }

    public int Count => profiles != null ? profiles.Length : 0;

#if UNITY_EDITOR
    // helper for Bakers: finds catalog and returns profile id
    // editor only, Baker runs only in editor
    public static int FindProfileIdInProject(WeaponProfileSO profile)
    {
        if (profile == null) return -1;

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:WeaponCatalogAsset");
        for (int g = 0; g < guids.Length; g++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[g]);
            WeaponCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponCatalogAsset>(path);
            if (catalog == null) continue;
            if (catalog.TryGetProfileId(profile, out int id)) return id;
        }
        return -1;
    }
#endif
}