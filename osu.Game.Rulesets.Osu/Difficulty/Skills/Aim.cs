// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : OsuStrainSkill
    {
        public Aim(Mod[] mods, bool withSliders)
            : base(mods)
        {
            this.withSliders = withSliders;
        }

        private readonly bool withSliders;

        private double currentFlowStrain;
        private double currentSnapStrain;
        private double realStrain;

        private double skillMultiplier => 32.5;//38.75;
        // private double skillMultiplier => 23.55;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => realStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentFlowStrain *= strainDecay(current.DeltaTime);
            currentSnapStrain *= strainDecay(current.DeltaTime);

            (double snap, double flow) aimResult = AimEvaluator.EvaluateDifficultyOf(current, withSliders, strainDecayBase);

            double flowStrain = aimResult.flow * skillMultiplier;
            double snapStrain = aimResult.snap * skillMultiplier;

            if (flowStrain < snapStrain)
                currentFlowStrain += flowStrain;
            else
                currentSnapStrain += snapStrain;

            double p = 3;
            realStrain = currentFlowStrain + currentSnapStrain + (Math.Pow(Math.Pow(currentFlowStrain, p) + Math.Pow(currentSnapStrain, p), 1.0 / p) - Math.Max(currentFlowStrain, currentSnapStrain));

            return realStrain;
        }
    }
}
