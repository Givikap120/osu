// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class CombinedAim : Aim
    {
        public CombinedAim(Mod[] mods, bool includeSliders)
            : base(mods, includeSliders)
        {
        }

        private List<double> objectSnapinness = [];

        protected override double StrainValueOf(DifficultyHitObject current)
        {
            double snap = AimEvaluator.EvaluateDifficultyOf(current, IncludeSliders);
            double flow = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders);

            double snappiness = snap > 0 ? Math.Pow(flow / snap, 1.5) : 0;

            // Rescale
            snappiness = (snappiness - 0.25) / (1 - 0.25);
            objectSnapinness.Add(Math.Clamp(snappiness, 0, 1));

            return Math.Min(snap, flow);
        }

        public override double CountRelevantObjects()
        {
            double consistentTopStrain = DifficultyValue() / 10; // What would the top strain be if all strain values were identical
            if (consistentTopStrain == 0)
                return 0.0;

            // Being consistently difficult for 1000 notes should be worth more than being consistently difficult for 100.
            double totalStrains = ObjectDifficulties.Count;
            double lengthFactor = 0.73 * Math.Pow(0.998759, totalStrains);

            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return ObjectDifficulties.Zip(objectSnapinness, (strain, snapinness) =>
                (0.5 + 0.5 * snapinness) * (1.0 - lengthFactor) / (1 + Math.Exp(-10 * (strain / consistentTopStrain - 0.87 - lengthFactor / 4.0)))
            ).Sum();
        }
    }
}
