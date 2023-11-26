﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
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

        private double skillMultiplier => 31.6;//38.75;
        // private double skillMultiplier => 23.55;
        private double strainDecayBase => 0.15;

        private double strainDecay(double ms, double decayBase) => Math.Pow(decayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => currentTotalStrain * strainDecay(time - current.Previous(0).StartTime, strainDecayBase);
        protected double CalculateInitialStrain(ref double currentStrain, double time, DifficultyHitObject current) => currentStrain * strainDecay(time - current.Previous(0).StartTime, strainDecayBase);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double adjustedStrainDecay = AimEvaluator.AdjustStrainDecay(current, strainDecayBase);
            currentTotalStrain *= strainDecay(current.DeltaTime, adjustedStrainDecay);

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            currentTotalStrain += AimEvaluator.EvaluateDifficultyOf(current, withSliders, strainDecayBase, currentRhythm) * skillMultiplier;

            return currentTotalStrain;
        }

        protected (double total, double snap, double flow) CombinedStrainValueAt(DifficultyHitObject current)
        {
            const double min_snap_threshold = 0, min_flow_threshold = 0.34;

            double adjustedStrainDecay = AimEvaluator.AdjustStrainDecay(current, strainDecayBase);
            currentTotalStrain *= strainDecay(current.DeltaTime, adjustedStrainDecay);
            currentSnapStrain *= strainDecay(current.DeltaTime, adjustedStrainDecay);
            currentFlowStrain *= strainDecay(current.DeltaTime, adjustedStrainDecay);

            // currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);
            (double snap, double flow) objectDifficulties = AimEvaluator.EvaluateRawDifficultiesOf(current);

            currentTotalStrain += AimEvaluator.EvaluateTotalStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;

            double snapStrain = AimEvaluator.EvaluateSnapStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;
            double flowStrain = AimEvaluator.EvaluateFlowStrainOf(current, withSliders, strainDecayBase, objectDifficulties) * skillMultiplier;

            currentSnapStrain += Math.Max(snapStrain, flowStrain * min_snap_threshold);
            currentFlowStrain += Math.Max(flowStrain, snapStrain * min_flow_threshold);

            return (currentTotalStrain, currentSnapStrain, currentFlowStrain);
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

            var strains = CombinedStrainValueAt(current);

            CurrentSectionPeak = Math.Max(strains.total, CurrentSectionPeak);
            CurrentSnapSectionPeak = Math.Max(strains.snap, CurrentSnapSectionPeak);
            CurrentFlowSectionPeak = Math.Max(strains.flow, CurrentFlowSectionPeak);
        }

        public IEnumerable<double> GetCurrentSnapStrainPeaks() => SnapStrainPeaks.Append(CurrentSnapSectionPeak);
        public IEnumerable<double> GetCurrentFlowStrainPeaks() => FlowStrainPeaks.Append(CurrentFlowSectionPeak);

        private double logarithmicSummation(IEnumerable<double> strains, bool nerfDiffspikes)
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

            int index = 0;
            //double weight = 1;
            double difficulty = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            foreach (double strain in strainsList)
            {
                double weight = (1.0 + (20.0 / (1 + index))) / (Math.Pow(index, 0.9) + 1.0 + (20.0 / (1.0 + index)));

                difficulty += strain * weight;

                //weight *= DecayWeight;
                index += 1;
            }

            return difficulty;
        }

        private double geometricSummation(IEnumerable<double> strains, bool nerfDiffspikes)
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
                difficulty += strain * weight;
                weight *= DecayWeight;
            }

            return difficulty;
        }

        private const double mixed_aim_part = 0.22;
        public override double DifficultyValue()
        {
            double totalDifficulty = logarithmicSummation(GetCurrentStrainPeaks(), true);
            double snapDifficulty = logarithmicSummation(GetCurrentSnapStrainPeaks(), true);
            double flowDifficulty = logarithmicSummation(GetCurrentFlowStrainPeaks(), true);

            double difficulty = totalDifficulty * (1 - mixed_aim_part) + (snapDifficulty + flowDifficulty) * mixed_aim_part;

            double debugFix = 34 / skillMultiplier - 1;
            Console.WriteLine($"Snap dificulty - {snapDifficulty:0}, Flow difficulty - {flowDifficulty:0}, Total difficulty - {totalDifficulty:0}, " +
                $"Result - {difficulty:0} (+{100 * (difficulty / totalDifficulty - debugFix - 1):0.00}%)");

            return difficulty * DifficultyMultiplier;
        }
    }
}
