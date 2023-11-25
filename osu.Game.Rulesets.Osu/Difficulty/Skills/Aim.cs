// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Framework.Utils;
using osu.Game.Beatmaps;

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

        private double skillMultiplier => 34;//38.75;
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

            // currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);
            (double snap, double flow) objectDifficulties = AimEvaluator.EvaluateRawDifficultiesOf(current);

            currentTotalStrain += AimEvaluator.EvaluateTotalStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;
            currentSnapStrain += AimEvaluator.EvaluateSnapStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;
            currentFlowStrain += AimEvaluator.EvaluateFlowStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;

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

        private List<List<double>> transpose(List<List<double>> list)
        {
            int rowCount = list[0].Count;
            int colCount = list.Count;

            List<List<double>> transposedList = new List<List<double>>();

            for (int i = 0; i < rowCount; i++)
            {
                List<double> newRow = new List<double>();

                for (int j = 0; j < colCount; j++)
                {
                    newRow.Add(list[j][i]);
                }

                transposedList.Add(newRow);
            }

            return transposedList;
        }

        private double calculateDifficultyFromStrains(IEnumerable<double> strains, bool nerfDiffspikes, double decayWeight)
        {
            List<double> strainsList = strains.Where(x => x > 0).OrderByDescending(x => x).ToList();

            if (nerfDiffspikes)
            {
                // We are reducing the highest strains first to account for extreme difficulty spikes
                for (int i = 0; i < Math.Min(strainsList.Count, ReducedSectionCount); i++)
                {
                    double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((float)i / ReducedSectionCount, 0, 1)));
                    strainsList[i] *= Interpolation.Lerp(ReducedStrainBaseline, 1.0, scale);
                }

                strains = strains.OrderByDescending(x => x).ToList();
            }

            //int index = 0;
            double weight = 1;
            double difficulty = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            foreach (double strain in strainsList)
            {
                //double weight = (1.0 + (20.0 / (1 + index))) / (Math.Pow(index, 0.9) + 1.0 + (20.0 / (1.0 + index)));

                difficulty += strain * weight;

                weight *= DecayWeight;
                //index += 1;
            }

            double decayAdjustMultiplier = (1 - decayWeight) / (1 - DecayWeight);
            return difficulty * decayAdjustMultiplier;
        }
        public override double DifficultyValue()
        {
            const double mixed_aim_part = 0.2;
            const double snap_difficulty_multiplier = 1.2, snap_decay = 0.94;
            const double flow_difficulty_multiplier = 1, flow_decay = 0.9;

            double totalDifficulty = calculateDifficultyFromStrains(GetCurrentStrainPeaks(), true, DecayWeight);
            double snapDifficulty = calculateDifficultyFromStrains(GetCurrentSnapStrainPeaks(), true, snap_decay) * snap_difficulty_multiplier;
            double flowDifficulty = calculateDifficultyFromStrains(GetCurrentFlowStrainPeaks(), true, flow_decay) * flow_difficulty_multiplier;

            double difficulty = totalDifficulty * (1 - mixed_aim_part) + (snapDifficulty + flowDifficulty) * mixed_aim_part;
            Console.WriteLine($"Snap dificulty - {snapDifficulty}, Flow difficulty - {flowDifficulty}, Total difficulty - {totalDifficulty}, Result - {difficulty}");

            return difficulty * DifficultyMultiplier;
        }

    }
}
