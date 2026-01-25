// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class ReadingLowAR : StrainSkill
    {
        private double skillMultiplier => 1.22;
        private double aimComponentMultiplier => 0.4;

        public ReadingLowAR(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecayBase => 0.15;
        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        private double currentDensityAimStrain = 0;

        private readonly List<double> sliderStrains = new List<double>();

        protected override double ProcessInternal(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            double densityReadingDifficulty = ReadingEvaluator.EvaluateDifficultyOf(current);
            double densityAimingFactor = ReadingEvaluator.EvaluateAimingDensityFactorOf(current);

            double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current, true);
            aimDifficulty = Math.Min(aimDifficulty, 2 * AimEvaluator.EvaluateDifficultyOf(current, false)) * (1 - decay); // Reward sliders, but cap at 2x

            currentDensityAimStrain *= decay;
            currentDensityAimStrain += densityAimingFactor * aimDifficulty * aimComponentMultiplier;

            double totalDensityDifficulty = (currentDensityAimStrain + densityReadingDifficulty) * skillMultiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalDensityDifficulty);

            // Strain display support, visual-only
            if (current.Index == 0)
                CurrentSectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > CurrentSectionEnd)
            {
                StrainPeaks.Add(CurrentSectionPeak);
                CurrentSectionPeak = 0;
                CurrentSectionEnd += SectionLength;
            }

            CurrentSectionPeak = Math.Max(totalDensityDifficulty, CurrentSectionPeak);

            return totalDensityDifficulty;
        }

        private double reducedNoteCount => 5;
        private double reducedNoteBaseline => 0.7;
        public override double DifficultyValue()
        {
            // Sections with 0 difficulty are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These sections will not contribute to the difficulty.
            var peaks = ObjectDifficulties.Where(p => p > 0);

            List<double> values = peaks.OrderByDescending(d => d).ToList();

            for (int i = 0; i < Math.Min(values.Count, reducedNoteCount); i++)
            {
                double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp(i / reducedNoteCount, 0, 1)));
                values[i] *= Interpolation.Lerp(reducedNoteBaseline, 1.0, scale);
            }

            values = values.OrderByDescending(d => d).ToList();

            double difficulty = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            for (int i = 0; i < values.Count; i++)
            {
                difficulty += values[i] / (i + 1);
            }

            return difficulty;
        }

        public double CountTopWeightedSliders(double difficultyValue) => OsuStrainUtils.CountTopWeightedSliders(sliderStrains, difficultyValue);

        public static double DifficultyToPerformance(double difficulty) => Math.Max(
            Math.Max(Math.Pow(difficulty, 1.5) * 20, Math.Pow(difficulty, 2) * 17.0),
            Math.Max(Math.Pow(difficulty, 3) * 10.5, Math.Pow(difficulty, 4) * 6.00));

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }
    }

    public class ReadingHidden : Aim
    {
        public ReadingHidden(Mod[] mods)
            : base(mods, false)
        {
        }
        protected new double SkillMultiplier => 7.3;

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double decay = StrainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            CurrentStrain *= decay;

            // We're not using slider aim because we assuming that HD doesn't makes sliders harder (what is not true, but we will ignore this for now)
            double hiddenDifficulty = AimEvaluator.EvaluateDifficultyOf(current, false) * (1 - decay);
            hiddenDifficulty *= ReadingHiddenEvaluator.EvaluateDifficultyOf(current);
            hiddenDifficulty *= SkillMultiplier;

            CurrentStrain += hiddenDifficulty;

            return CurrentStrain;
        }

        public new static double DifficultyToPerformance(double difficulty) => Math.Max(
            Math.Max(difficulty * 16, Math.Pow(difficulty, 2) * 10), Math.Pow(difficulty, 3) * 4);
    }

    public class ReadingHighAR : StrainSkill
    {
        public const double MECHANICAL_PP_POWER = 0.6;
        private const double skill_multiplier = 9.31;
        private const double component_default_value_multiplier = 280;
        public ReadingHighAR(Mod[] mods)
            : base(mods)
        {
            aimComponent = new HighARAimComponent(mods);
            speedComponent = new HighARSpeedComponent(mods);
        }

        private HighARAimComponent aimComponent;
        private HighARSpeedComponent speedComponent;

        protected override double ProcessInternal(DifficultyHitObject current)
        {
            aimComponent.Process(current);
            speedComponent.Process(current);

            if (current.Index == 0)
                CurrentSectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > CurrentSectionEnd)
            {
                StrainPeaks.Add(CurrentSectionPeak);
                CurrentSectionPeak = 0;
                CurrentSectionEnd += SectionLength;
            }

           // double visualDifficultyValue = scaleDifficulty(aimComponent.CurrentSectionPeak, speedComponent.CurrentSectionPeak);
            double visualDifficultyValue = aimComponent.CurrentSectionPeak;

            CurrentSectionPeak = Math.Max(visualDifficultyValue, CurrentSectionPeak);
            return visualDifficultyValue;
        }

        private static double performanceToDifficulty(double performance) => Math.Pow(performance / 4, 1.0 / 3);

        private static double scaleDifficulty(double aimPart, double speedPart)
        {
            // Simulating summing to get the most correct value possible
            double aimValue = Math.Sqrt(aimPart * skill_multiplier) * OsuRatingCalculator.DIFFICULTY_MULTIPLIER;
            double speedValue = Math.Sqrt(speedPart * skill_multiplier) * OsuRatingCalculator.DIFFICULTY_MULTIPLIER;

            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimValue);
            double speedPerformance = HarmonicSkill.DifficultyToPerformance(speedValue);

            double sumPower = OsuDifficultyCalculator.SUM_POWER;
            double totalPerformance = Math.Pow(Math.Pow(aimPerformance, sumPower) + Math.Pow(speedPerformance, sumPower), 1.0 / sumPower);

            double newSkillValue = performanceToDifficulty(totalPerformance);
            double difficultyValue = Math.Pow(newSkillValue / OsuRatingCalculator.DIFFICULTY_MULTIPLIER, 2.0);

            difficultyValue = Math.Pow(difficultyValue / skill_multiplier, MECHANICAL_PP_POWER);

            return difficultyValue;
        }

        public override double DifficultyValue() => skill_multiplier * scaleDifficulty(aimComponent.DifficultyValue(), speedComponent.DifficultyValue());

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current)
        {
            throw new NotImplementedException();
        }

        public class HighARAimComponent : Aim
        {
            public HighARAimComponent(Mod[] mods)
                : base(mods, true)
            {
            }

            protected override double StrainValueAt(DifficultyHitObject current)
            {
                double decay = StrainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
                CurrentStrain *= decay;

                double highARDifficulty = Math.Pow(ReadingHighAREvaluator.EvaluateDifficultyOf(current, true), 1.0 / MECHANICAL_PP_POWER);
                double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current, true) * (1 - decay) * SkillMultiplier;

                aimDifficulty *= highARDifficulty;
                CurrentStrain += aimDifficulty;

                return CurrentStrain + component_default_value_multiplier * highARDifficulty * DifficultyCalculationUtils.ReverseLerp(current.Index, 0, 200);
            }
        }

        public class HighARSpeedComponent : Speed
        {
            public HighARSpeedComponent(Mod[] mods)
                : base(mods)
            {
            }

            protected override double ObjectDifficultyOf(DifficultyHitObject current)
            {
                OsuDifficultyHitObject currObj = (OsuDifficultyHitObject)current;

                double decay = StrainDecay(currObj.AdjustedDeltaTime);
                CurrentDifficulty *= decay;

                double highARDifficulty = Math.Pow(ReadingHighAREvaluator.EvaluateDifficultyOf(current, false), 1.0 / MECHANICAL_PP_POWER);
                double speedDifficulty = SpeedEvaluator.EvaluateDifficultyOf(current, Mods) * (1 - decay) * SkillMultiplier;

                speedDifficulty *= highARDifficulty;
                CurrentDifficulty += speedDifficulty;

                double currentRhythm = currObj.RhythmDifficulty;
                double totalStrain = speedDifficulty * currentRhythm;
                return totalStrain + component_default_value_multiplier * highARDifficulty * DifficultyCalculationUtils.ReverseLerp(current.Index, 0, 200);
            }
        }
    }
}
