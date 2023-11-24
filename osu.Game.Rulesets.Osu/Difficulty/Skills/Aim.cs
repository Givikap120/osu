// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Framework.Utils;

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

        protected readonly List<double> SnapStrainPeaks = new List<double>();
        protected readonly List<double> FlowStrainPeaks = new List<double>();

        private readonly bool withSliders;

        private double currentTotalStrain;
        private double currentSnapStrain;
        private double currentFlowStrain;

        protected double CurrentSnapSectionPeak;
        protected double CurrentFlowSectionPeak;

        private double skillMultiplier => 32;//38.75;
        // private double skillMultiplier => 23.55;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentTotalStrain * strainDecay(time - current.Previous(0).StartTime);
        protected double CalculateInitialStrain(ref double currentStrain, double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            currentTotalStrain *= strainDecay(current.DeltaTime);

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            currentTotalStrain += AimEvaluator.EvaluateDifficultyOf(current, withSliders, strainDecayBase, currentRhythm) * skillMultiplier;

            return currentTotalStrain;
        }

        protected double StrainValueAt(DifficultyHitObject current, out double snapStrain, out double flowStrain)
        {
            currentTotalStrain *= strainDecay(current.DeltaTime);
            currentSnapStrain *= strainDecay(current.DeltaTime);
            currentFlowStrain *= strainDecay(current.DeltaTime);

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);
            double[] snapflowInfo = new double[2];

            currentTotalStrain += AimEvaluator.EvaluateDifficultyOf(current, withSliders, strainDecayBase, currentRhythm, snapflowInfo) * skillMultiplier;
            currentSnapStrain += snapflowInfo[0];
            currentFlowStrain += snapflowInfo[1];

            snapStrain = currentSnapStrain;
            flowStrain = currentFlowStrain;
            return currentTotalStrain;
        }

        protected void StartNewSnapSectionFrom(double time, DifficultyHitObject current)
        {
            // The maximum strain of the new section is not zero by default
            // This means we need to capture the strain level at the beginning of the new section, and use that as the initial peak level.
            CurrentSnapSectionPeak = CalculateInitialStrain(ref currentSnapStrain, time, current);
        }

        protected void StartNewFlowSectionFrom(double time, DifficultyHitObject current)
        {
            // The maximum strain of the new section is not zero by default
            // This means we need to capture the strain level at the beginning of the new section, and use that as the initial peak level.
            CurrentFlowSectionPeak = CalculateInitialStrain(ref currentFlowStrain, time, current);
        }

        protected void SaveCurrentPeak(List<double> peaks, double currentPeak)
        {
            peaks.Add(currentPeak);
        }
        public override void Process(DifficultyHitObject current)
        {
            // The first object doesn't generate a strain, so we begin with an incremented section end
            if (current.Index == 0)
                CurrentSectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > CurrentSectionEnd)
            {
                SaveCurrentPeak();
                SaveCurrentPeak(SnapStrainPeaks, CurrentSnapSectionPeak);
                SaveCurrentPeak(FlowStrainPeaks, CurrentFlowSectionPeak);

                StartNewSectionFrom(CurrentSectionEnd, current);
                StartNewSnapSectionFrom(CurrentSectionEnd, current);
                StartNewFlowSectionFrom(CurrentSectionEnd, current);

                CurrentSectionEnd += SectionLength;
            }

            double totalStrain = StrainValueAt(current, out double snapStrain, out double flowStrain);

            CurrentSectionPeak = Math.Max(totalStrain, CurrentSectionPeak);
            CurrentSnapSectionPeak = Math.Max(snapStrain, CurrentSnapSectionPeak);
            CurrentFlowSectionPeak = Math.Max(flowStrain, CurrentFlowSectionPeak);
        }

        public IEnumerable<double> GetCurrentSnapStrainPeaks() => SnapStrainPeaks.Append(CurrentSnapSectionPeak);
        public IEnumerable<double> GetCurrentFlowStrainPeaks() => FlowStrainPeaks.Append(CurrentFlowSectionPeak);

        protected double GetMixedAimRatio()
        {
            double snapDifficulty = 0;
            double weight = 1;

            var snapPeaks = GetCurrentSnapStrainPeaks().Where(p => p > 0);
            foreach (double strain in snapPeaks.OrderByDescending(d => d))
            {
                snapDifficulty += strain * weight;
                weight *= DecayWeight;
            }

            double flowDifficulty = 0;
            weight = 1;

            var flowPeaks = GetCurrentFlowStrainPeaks().Where(p => p > 0);
            foreach (double strain in flowPeaks.OrderByDescending(d => d))
            {
                flowDifficulty += strain * weight;
                weight *= DecayWeight;
            }

            double ratio;

            if (snapDifficulty >= flowDifficulty)
            {
                ratio = Math.Pow(flowDifficulty / snapDifficulty, 2);
            }
            else
            {
                ratio = Math.Pow(snapDifficulty / flowDifficulty, 2.5);
            }

            return ratio;
        }
        public override double DifficultyValue()
        {
            double difficulty = 0;

            // Sections with 0 strain are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These sections will not contribute to the difficulty.
            var peaks = GetCurrentStrainPeaks().Where(p => p > 0);

            List<double> strains = peaks.OrderByDescending(d => d).ToList();

            // We are reducing the highest strains first to account for extreme difficulty spikes
            for (int i = 0; i < Math.Min(strains.Count, ReducedSectionCount); i++)
            {
                double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((float)i / ReducedSectionCount, 0, 1)));
                strains[i] *= Interpolation.Lerp(ReducedStrainBaseline, 1.0, scale);
            }

            int index = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            foreach (double strain in strains.OrderByDescending(d => d))
            {
                // Below uses harmonic sum scaling which makes the resulting summation logarithmic rather than geometric.
                // Good for properly weighting difficulty across full map instead of using object count for LengthBonus.
                // 1.44 and 7.5 are arbitrary constants that worked well.
                // double weight = 1.44 * ((1 + (7.5 / (1 + index))) / (index + 1 + (7.5 / (1 + index))));
                // double weight = 1.42 * ((1 + (7.5 / (1 + index))) / (Math.Pow(index, Math.Max(0.85, 1.0 - 0.15 * Math.Pow(index / 1500.0, 1))) + 1 + (7.5 / (1 + index))));
                double weight = (1.0 + (20.0 / (1 + index))) / (Math.Pow(index, 0.9) + 1.0 + (20.0 / (1.0 + index)));

                difficulty += strain * weight;
                index += 1;
            }

            difficulty *= DifficultyMultiplier;

            const double degree = 4;

            double lesserDifficulty = difficulty * GetMixedAimRatio();
            double adjustedDifficulty = Math.Pow(Math.Pow(difficulty, degree) + Math.Pow(lesserDifficulty, degree), 1.0 / degree);

            return adjustedDifficulty;
        }
    }
}
