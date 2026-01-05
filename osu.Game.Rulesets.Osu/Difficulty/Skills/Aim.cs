// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : OsuStrainSkill
    {
        public readonly bool IncludeSliders;

        public Aim(Mod[] mods, bool includeSliders)
            : base(mods)
        {
            IncludeSliders = includeSliders;
        }


        protected double CurrentStrain;
        protected double SkillMultiplier => 26;

        private readonly List<double> sliderStrains = new List<double>();

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => CurrentStrain * StrainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            CurrentStrain *= StrainDecay(current.DeltaTime);
            CurrentStrain += AimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * SkillMultiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(CurrentStrain);

            return CurrentStrain;
        }

        public double GetDifficultSliders()
        {
            if (sliderStrains.Count == 0)
                return 0;

            double maxSliderStrain = sliderStrains.Max();

            if (maxSliderStrain == 0)
                return 0;

            return sliderStrains.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxSliderStrain * 12.0 - 6.0))));
        }

        public double CountTopWeightedSliders(double difficultyValue)
            => OsuStrainUtils.CountTopWeightedSliders(sliderStrains, difficultyValue);
    }
}
