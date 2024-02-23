﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class ReadingHighAR : GraphSkill
    {
        public ReadingHighAR(Mod[] mods)
            : base(mods)
        {
            aimComponent = new HighARAimComponent(mods);
            speedComponent = new HighARSpeedComponent(mods);

            aimComponentNoAdjust = new HighARAimComponent(mods, false);
        }

        private HighARAimComponent aimComponent;
        private HighARAimComponent aimComponentNoAdjust;
        private HighARSpeedComponent speedComponent;

        private readonly List<double> difficulties = new List<double>();
        private int objectsCount = 0;
        private double preempt = -1;

        public override void Process(DifficultyHitObject current)
        {
            if (preempt < 0) preempt = ((OsuDifficultyHitObject)current).Preempt;

            aimComponent.Process(current);
            speedComponent.Process(current);

            aimComponentNoAdjust.Process(current);

            if (current.BaseObject is not Spinner)
                objectsCount++;

            double power = OsuDifficultyCalculator.SUM_POWER;
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
            // Simulating summing to get the most correct value possible
            double aimValue = Math.Sqrt(aimComponent.DifficultyValue()) * OsuDifficultyCalculator.DIFFICULTY_MULTIPLIER;
            double speedValue = Math.Sqrt(speedComponent.DifficultyValue()) * OsuDifficultyCalculator.DIFFICULTY_MULTIPLIER;

            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimValue);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(speedValue);

            double power = OsuDifficultyCalculator.SUM_POWER;
            double totalPerformance = Math.Pow(Math.Pow(aimPerformance, power) + Math.Pow(speedPerformance, power), 1.0 / power);

            // First half of length bonus is in SR to not inflate Star Rating short AR11 maps
            double lengthBonus = OsuPerformanceCalculator.CalculateDefaultLengthBonus(objectsCount);
            totalPerformance *= lengthBonus;

            double adjustedDifficulty = OsuStrainSkill.PerformanceToDifficulty(totalPerformance);
            double difficultyValue = Math.Pow(adjustedDifficulty / OsuDifficultyCalculator.DIFFICULTY_MULTIPLIER, 2.0);

            // have the same value as difficultyValue at 500pp point
            return 75 * Math.Sqrt(difficultyValue);
        }
    }

    public class HighARAimComponent : OsuStrainSkill
    {
        public HighARAimComponent(Mod[] mods, bool adjustHighAR = true)
            : base(mods)
        {
            this.adjustHighAR = adjustHighAR;
        }

        private bool adjustHighAR;
        private double currentStrain;

        private double skillMultiplier => 17.8;
        private double defaultValueMultiplier => 30;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentStrain *= strainDecay(current.DeltaTime);

            double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current, true);
            double readingDifficulty = ReadingHighAREvaluator.EvaluateDifficultyOf(current, adjustHighAR);
            aimDifficulty *= Math.Pow(readingDifficulty, 2);
            aimDifficulty *= skillMultiplier;

            currentStrain += aimDifficulty;

            return currentStrain + defaultValueMultiplier * ReadingHighAREvaluator.EvaluateDifficultyOf(current, adjustHighAR);
        }
    }

    public class HighARSpeedComponent : OsuStrainSkill
    {
        private double skillMultiplier => 850;
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
            OsuDifficultyHitObject currODHO = (OsuDifficultyHitObject)current;

            currentStrain *= strainDecay(currODHO.StrainTime);

            double speedDifficulty = SpeedEvaluator.EvaluateDifficultyOf(current) * skillMultiplier;
            speedDifficulty *= Math.Pow(ReadingHighAREvaluator.EvaluateDifficultyOf(current, false), 2);
            currentStrain += speedDifficulty;

            currentRhythm = currODHO.RhythmDifficulty;
            double totalStrain = currentStrain * currentRhythm;
            return totalStrain;
        }
    }
}
