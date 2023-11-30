using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class TotalAim : BaseAim
    {
        public TotalAim(Mod[] mods, bool withSliders)
            : base(mods, withSliders)
        {
        }
        protected override double StrainValueAt(DifficultyHitObject current)
        {
            PriorStrain *= StrainDecay(current.DeltaTime);
            (double snap, double flow) objectDifficulties = AimEvaluator.EvaluateRawDifficultiesOf(current);
            double currentObjectDifficulty = AimEvaluator.EvaluateTotalStrainOf(current, WithSliders, StrainDecayBase, objectDifficulties) * CURRENT_STRAIN_MULTIPLIER;
            double totalDifficulty = PriorStrain * PRIOR_STRAIN_MULTIPLIER + currentObjectDifficulty;
            PriorStrain += currentObjectDifficulty;
            return totalDifficulty;
        }
    }
}
