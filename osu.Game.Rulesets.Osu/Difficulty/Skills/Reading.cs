// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{

    public class ReadingLowAR : GraphSkill
    {
        private readonly List<double> difficulties = new List<double>();
        private const double skill_multiplier = 2.4;

        public ReadingLowAR(Mod[] mods)
            : base(mods)
        {
        }

        public override void Process(DifficultyHitObject current)
        {
            double currentDifficulty = ReadingEvaluator.EvaluateDensityDifficultyOf(current) * skill_multiplier;

            difficulties.Add(currentDifficulty);

            if (current.Index == 0)
                CurrentSectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > CurrentSectionEnd)
            {
                StrainPeaks.Add(CurrentSectionPeak);
                CurrentSectionPeak = 0;
                CurrentSectionEnd += SectionLength;
            }

            CurrentSectionPeak = Math.Max(currentDifficulty, CurrentSectionPeak);
        }

        public override double DifficultyValue()
        {
            double difficulty = 0;

            // Sections with 0 difficulty are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These sections will not contribute to the difficulty.
            var peaks = difficulties.Where(p => p > 0);

            List<double> values = peaks.OrderByDescending(d => d).ToList();

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            for (int i = 0; i < values.Count; i++)
            {
                difficulty += values[i] / (i + 1);
            }

            return difficulty;
        }
    }

    public class ReadingHighAR : GraphSkill
    {
        public ReadingHighAR(Mod[] mods)
            : base(mods)
        {
            aimComponent = new HighARAimComponent(mods);
            speedComponent = new HighARSpeedComponent(mods);
        }

        private HighARAimComponent aimComponent;
        private HighARSpeedComponent speedComponent;

        private readonly List<double> difficulties = new List<double>();

        public override void Process(DifficultyHitObject current)
        {
            aimComponent.Process(current);
            speedComponent.Process(current);

            double power = OsuDifficultyCalculator.SumPower;
            double mergedDifficulty = Math.Pow(
                Math.Pow(aimComponent.CurrentSectionPeak, power) +
                Math.Pow(speedComponent.CurrentSectionPeak, power), 1.0 / power);

            difficulties.Add(mergedDifficulty);

            if (current.Index == 0)
                CurrentSectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > CurrentSectionEnd)
            {
                StrainPeaks.Add(CurrentSectionPeak);
                CurrentSectionPeak = 0;
                CurrentSectionEnd += SectionLength;
            }

            CurrentSectionPeak = Math.Max(mergedDifficulty, CurrentSectionPeak);
        }
        public override double DifficultyValue()
        {
            double power = OsuDifficultyCalculator.SumPower;
            return Math.Pow(
                Math.Pow(aimComponent.DifficultyValue(), power) +
                Math.Pow(speedComponent.DifficultyValue(), power), 1.0 / power);
        }
    }

    public class HighARAimComponent : OsuStrainSkill
    {
        public HighARAimComponent(Mod[] mods)
            : base(mods)
        {
        }

        private double currentStrain;
        // private double currentRhythm;

        private double skillMultiplier => 12;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain *= strainDecay(current.DeltaTime);

            double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current, true);
            aimDifficulty *= ReadingEvaluator.EvaluateHighARDifficultyOf(current);
            currentStrain += aimDifficulty * skillMultiplier;

            // currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalStrain = currentStrain;
            return totalStrain;
        }
    }

    public class HighARSpeedComponent : OsuStrainSkill
    {
        private double skillMultiplier => 900;
        private double strainDecayBase => 0.3;

        private double currentStrain;
        private double currentRhythm;

        public HighARSpeedComponent(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => (currentStrain * currentRhythm) * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain *= strainDecay(((OsuDifficultyHitObject)current).StrainTime);

            double speedDifficulty = SpeedEvaluator.EvaluateDifficultyOf(current) * skillMultiplier;
            speedDifficulty *= ReadingEvaluator.EvaluateHighARDifficultyOf(current);
            currentStrain += speedDifficulty;

            currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);
            // currentRhythm *= currentRhythm; // Squaring is broken cuz rhythm is broken ((((

            double totalStrain = currentStrain * currentRhythm;
            return totalStrain;
        }
    }

    public class ReadingHidden : OsuStrainSkill
    {
        public ReadingHidden(Mod[] mods)
            : base(mods)
        {
        }

        private double currentStrain;

        private double skillMultiplier => 5;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain *= strainDecay(current.DeltaTime);

            double hdDifficulty = 0;

            currentStrain += hdDifficulty * skillMultiplier;
            return currentStrain;
        }
    }
}
