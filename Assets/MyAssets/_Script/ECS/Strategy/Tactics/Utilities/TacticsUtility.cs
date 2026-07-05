public static class TacticsUtility
{
    public static TacticsModifiers Get(Tactics tactics)
    {
        switch (tactics)
        {
            case Tactics.Evasive:
                return new TacticsModifiers
                {
                    anchorLeashMul = 0.65f,
                    priorityHintWeightMul = 0.75f,
                    arrivalRadiusMul = 0.8f,
                    holdTightness = 0.9f,
                };

            case Tactics.Aggressive:
                return new TacticsModifiers
                {
                    anchorLeashMul = 1.5f,
                    priorityHintWeightMul = 1.25f,
                    arrivalRadiusMul = 1.2f,
                    holdTightness = 0.3f,
                };

            case Tactics.Neutral:
            default:
                return new TacticsModifiers
                {
                    anchorLeashMul = 1.0f,
                    priorityHintWeightMul = 1.0f,
                    arrivalRadiusMul = 1.0f,
                    holdTightness = 0.6f,
                };
        }
    }
}
