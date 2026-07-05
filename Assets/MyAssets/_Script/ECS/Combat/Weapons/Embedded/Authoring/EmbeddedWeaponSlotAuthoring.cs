using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EmbeddedWeaponSlotAuthoring : MonoBehaviour
{
    [Header("Action Profile")]
    [Tooltip("Flexible support/action setup: Targeting -> Delivery -> Effect. Empty means this slot is a normal damage weapon configured below.")]
    public EmbeddedActionProfileSO actionProfile;

    [Header("Damage Weapon")]
    [Tooltip("Used only when Action Profile is empty. Normal embedded damage weapon profile.")]
    public WeaponProfileSO profile;

    [Tooltip("Used only when Action Profile is empty. Projectile/rocket/hitscan prefab for the normal damage weapon.")]
    public GameObject ammoGameObject;

    [Header("Muzzle / Beam Spawn")]
    [Tooltip("Projectile/beam spawn point. If Muzzle Points is empty, this is used. If null, the slot GameObject position is used.")]
    public Transform muzzlePoint;

    [Tooltip("Optional hardpoints. Damage: SequentialHardpoints fires these one by one; SimultaneousHardpoints fires all at once. Action: first/all non-null points are used as beam/aura spawn sources.")]
    public Transform[] muzzlePoints;

    [Header("Optional Visual")]
    [Tooltip("Optional visual root for a rotating embedded weapon/support slot. Leave empty when the weapon is drawn directly into the ship sprite/mesh.")]
    public Transform visualRoot;

    [Tooltip("If enabled, Visual Root rotates with this embedded slot. Ignored when Visual Root is empty.")]
    public bool rotateVisual = true;

    private void OnValidate()
    {
        if (!EmbeddedWeaponAuthoringValidationUtility.TryValidateSlot(this, out string error))
        {
            Debug.LogError($"EmbeddedWeaponSlotAuthoring '{name}': {error}", this);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        EmbeddedActionDeliveryKind deliveryKind = EmbeddedActionAuthoringUtility.ResolveDeliveryKind(this);
        EmbeddedActionEffectKind effectKind = EmbeddedActionAuthoringUtility.ResolveEffectKind(this);
        WeaponProfileSO resolvedProfile = EmbeddedActionAuthoringUtility.ResolveWeaponProfile(this);
        if (deliveryKind == EmbeddedActionDeliveryKind.WeaponProfile && resolvedProfile == null)
        {
            return;
        }

        float range = EmbeddedActionAuthoringUtility.ResolveRange(this);
        if (range <= 0f)
        {
            return;
        }

        Vector3 pivot = transform.position;
        Vector3 normal = Vector3.forward;
        Vector3 forward = transform.up;
        Vector3 mainMuzzle = GetPrimaryMuzzlePosition();

        bool isSupport = effectKind != EmbeddedActionEffectKind.Damage || deliveryKind != EmbeddedActionDeliveryKind.WeaponProfile;
        Handles.color = isSupport
            ? new Color(0f, 1f, 0.5f, 0.10f)
            : new Color(1f, 0.5f, 0f, 0.10f);
        Handles.DrawSolidDisc(mainMuzzle, normal, range);

        Handles.color = isSupport
            ? new Color(0f, 1f, 0.5f, 1f)
            : new Color(1f, 0.5f, 0f, 1f);
        Handles.DrawWireDisc(mainMuzzle, normal, range);

        DrawForwardGizmo(mainMuzzle, forward, range);
        DrawRotationSectorGizmo(pivot, normal, forward, range);
        DrawAllMuzzleGizmos(pivot);

        string label;
        if (isSupport)
        {
            label = $"Embedded {effectKind}/{deliveryKind}: {range:0.##}";
        }
        else
        {
            WeaponFirePattern firePattern = resolvedProfile != null ? resolvedProfile.firePattern : WeaponFirePattern.Single;
            string pattern = firePattern == WeaponFirePattern.SequentialHardpoints ? "seq" :
                firePattern == WeaponFirePattern.SimultaneousHardpoints ? "sim" : "single";
            label = $"Embedded {pattern}: {range:0.##}";
        }

        Handles.Label(mainMuzzle + Vector3.up * 0.2f, label);
    }

    private Vector3 GetPrimaryMuzzlePosition()
    {
        if (muzzlePoints != null)
        {
            for (int i = 0; i < muzzlePoints.Length; i++)
            {
                if (muzzlePoints[i] != null)
                {
                    return muzzlePoints[i].position;
                }
            }
        }

        return muzzlePoint != null ? muzzlePoint.position : transform.position;
    }

    private void DrawForwardGizmo(Vector3 muzzle, Vector3 forward, float range)
    {
        Vector3 safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.up;
        float length = Mathf.Min(range, 2f);

        Handles.color = new Color(0f, 1f, 0.2f, 1f);
        Handles.DrawLine(muzzle, muzzle + safeForward * length);
        Handles.ConeHandleCap(0, muzzle + safeForward * length, Quaternion.LookRotation(safeForward, Vector3.forward), 0.18f, EventType.Repaint);
    }

    private void DrawAllMuzzleGizmos(Vector3 pivot)
    {
        bool drewAny = false;
        if (muzzlePoints != null)
        {
            for (int i = 0; i < muzzlePoints.Length; i++)
            {
                Transform muzzle = muzzlePoints[i];
                if (muzzle == null)
                {
                    continue;
                }

                DrawOneMuzzleGizmo(pivot, muzzle.position, i);
                drewAny = true;
            }
        }

        if (!drewAny)
        {
            DrawOneMuzzleGizmo(pivot, muzzlePoint != null ? muzzlePoint.position : pivot, 0);
        }
    }

    private void DrawOneMuzzleGizmo(Vector3 pivot, Vector3 muzzle, int index)
    {
        Handles.color = new Color(1f, 1f, 0f, 1f);
        Handles.SphereHandleCap(0, muzzle, Quaternion.identity, 0.16f, EventType.Repaint);
        Handles.Label(muzzle + Vector3.right * 0.08f, $"M{index}");

        if ((muzzle - pivot).sqrMagnitude > 0.0001f)
        {
            Handles.color = new Color(1f, 1f, 0f, 0.85f);
            Handles.DrawLine(pivot, muzzle);
        }
    }

    private void DrawRotationSectorGizmo(Vector3 pivot, Vector3 normal, Vector3 forward, float range)
    {
        WeaponProfileSO resolvedProfile = EmbeddedActionAuthoringUtility.ResolveWeaponProfile(this);
        if (resolvedProfile == null || EmbeddedActionAuthoringUtility.ResolveEffectKind(this) != EmbeddedActionEffectKind.Damage)
        {
            return;
        }

        if (!resolvedProfile.rotate || !resolvedProfile.limitRotation || resolvedProfile.rotationLimitAngle >= 179.9f)
        {
            return;
        }

        float halfAngle = Mathf.Clamp(resolvedProfile.rotationLimitAngle, 0f, 180f);
        Vector3 safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.up;
        Vector3 from = Quaternion.AngleAxis(-halfAngle, normal) * safeForward;

        Handles.color = new Color(0f, 0.75f, 1f, 0.18f);
        Handles.DrawSolidArc(pivot, normal, from, halfAngle * 2f, range);

        Handles.color = new Color(0f, 0.75f, 1f, 1f);
        Handles.DrawWireArc(pivot, normal, from, halfAngle * 2f, range);
        Handles.DrawLine(pivot, pivot + from.normalized * range);
        Handles.DrawLine(pivot, pivot + (Quaternion.AngleAxis(halfAngle * 2f, normal) * from).normalized * range);
    }
#endif
}
