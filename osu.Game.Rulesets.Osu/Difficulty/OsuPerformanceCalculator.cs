// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuPerformanceCalculator : PerformanceCalculator
    {
        private bool removeRelaxAutopilotPp => true;
        private bool enableLazerAcc => false;
        private bool enableCSR => false;

        private bool usingClassicSliderAccuracy;

        private OsuDifficultyAttributes attributes = null!;
        private Mod[] mods = null!;

        private double accuracy;
        private int scoreMaxCombo;
        private int countGreat;
        private int countOk;
        private int countMeh;
        private int countMiss;

        private double effectiveMissCount;

        private double overallDifficulty;
        private double approachRate;

        private double aimEstimatedSliderBreaks;
        private double speedEstimatedSliderBreaks;

        public OsuPerformanceCalculator()
            : base(new OsuRuleset())
        {
        }


        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        {
            this.attributes = (OsuDifficultyAttributes)attributes;

            usingClassicSliderAccuracy = score.Mods.OfType<OsuModClassic>().Any(m => m.NoSliderHeadAccuracy.Value);

            mods = score.Mods;
            accuracy = score.Accuracy;
            scoreMaxCombo = score.MaxCombo;
            countGreat = score.Statistics.GetValueOrDefault(HitResult.Great);
            countOk = score.Statistics.GetValueOrDefault(HitResult.Ok);
            countMeh = score.Statistics.GetValueOrDefault(HitResult.Meh);
            countMiss = score.Statistics.GetValueOrDefault(HitResult.Miss);
            effectiveMissCount = countMiss;

            var difficulty = score.BeatmapInfo!.Difficulty.Clone();

            score.Mods.OfType<IApplicableToDifficulty>().ForEach(m => m.ApplyToDifficulty(difficulty));

            double clockRate = ModUtils.CalculateRateWithMods(score.Mods);

            HitWindows hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(difficulty.OverallDifficulty);

            approachRate = OsuDifficultyCalculator.CalculateRateAdjustedApproachRate(difficulty.ApproachRate, clockRate);
            overallDifficulty = OsuDifficultyCalculator.CalculateRateAdjustedOverallDifficulty(difficulty.OverallDifficulty, clockRate);

            if (removeRelaxAutopilotPp && score.Mods.Any(m => m is OsuModRelax || m is OsuModAutopilot))
            {
                return new OsuPerformanceAttributes
                {
                    EffectiveMissCount = countMiss
                };
            }

            if (enableCSR && (usingClassicSliderAccuracy || !enableLazerAcc) && this.attributes.SliderCount > 0)
            {
                double fullComboThreshold = attributes.MaxCombo - 0.1 * this.attributes.SliderCount;

                if (scoreMaxCombo < fullComboThreshold)
                    effectiveMissCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);

                effectiveMissCount = Math.Min(effectiveMissCount, countOk + countMeh + countMiss);
                effectiveMissCount = Math.Max(effectiveMissCount, countMiss);
            }

            // Custom multipliers for NoFail and SpunOut.
            double multiplier = 1.12f; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things

            if (mods.Any(m => m is OsuModNoFail))
                multiplier *= 0.90f;

            if (mods.Any(m => m is OsuModSpunOut))
                multiplier *= 0.95f;

            double aimValue = computeAimValue();
            double speedValue = computeSpeedValue();
            double accuracyValue = computeAccuracyValue();
            double totalValue =
                Math.Pow(
                    Math.Pow(aimValue, 1.1) +
                    Math.Pow(speedValue, 1.1) +
                    Math.Pow(accuracyValue, 1.1), 1.0 / 1.1
                ) * multiplier;

            return new OsuPerformanceAttributes
            {
                Aim = aimValue,
                Speed = speedValue,
                Accuracy = accuracyValue,
                EffectiveMissCount = effectiveMissCount,
                AimEstimatedSliderBreaks = aimEstimatedSliderBreaks,
                SpeedEstimatedSliderBreaks = speedEstimatedSliderBreaks,
                Total = totalValue
            };
        }

        private double computeAimValue()
        {
            double aimValue = Math.Pow(5.0f * Math.Max(1.0f, attributes.AimDifficulty / 0.0675f) - 4.0f, 3.0f) / 100000.0f;

            // Longer maps are worth more
            double lengthBonus = 0.95f + 0.4f * Math.Min(1.0f, totalHits / 2000.0f) +
                (totalHits > 2000 ? Math.Log10(totalHits / 2000.0f) * 0.5f : 0.0f);

            aimValue *= lengthBonus;

            // Penalize misses exponentially. This mainly fixes tag4 maps and the likes until a per-hitobject solution is available
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    aimValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.AimDifficultStrainCount);
                else
                    aimValue *= Math.Pow(0.97f, effectiveMissCount);
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                aimValue *= Math.Min(Math.Pow(scoreMaxCombo, 0.8f) / Math.Pow(attributes.MaxCombo, 0.8f), 1.0f);

            double approachRateFactor = 1.0f;
            if (approachRate > 10.33f)
                approachRateFactor += 0.45f * (approachRate - 10.33f);
            else if (approachRate < 8.0f)
            {
                // HD is worth more with lower ar!
                if (mods.Any(h => h is OsuModHidden))
                    approachRateFactor += 0.02f * (8.0f - approachRate);
                else
                    approachRateFactor += 0.01f * (8.0f - approachRate);
            }

            aimValue *= approachRateFactor;

            if (mods.Any(h => h is OsuModHidden))
                aimValue *= 1.18f;

            if (mods.Any(h => h is OsuModFlashlight))
            {
                // Add combo scaling for FL if CSR is enabled
                double comboScale = attributes.MaxCombo > 0 ? Math.Min(Math.Pow(scoreMaxCombo, 0.8) / Math.Pow(attributes.MaxCombo, 0.8), 1.0) : 0;
                double csrAdjust = enableCSR ? comboScale : 1.0;

                // Apply length bonus again if flashlight is on simply because it becomes a lot harder on longer maps.
                aimValue *= 1.0f + 0.45f * lengthBonus * csrAdjust;
            }

            // Scale the aim value with accuracy _slightly_
            aimValue *= 0.5f + accuracy / 2.0f;
            // It is important to also consider accuracy difficulty when doing that
            aimValue *= 0.98f + Math.Pow(overallDifficulty, 2) / 2500;

            return aimValue;
        }

        private double computeSpeedValue()
        {
            double speedValue = Math.Pow(5.0f * Math.Max(1.0f, attributes.SpeedDifficulty / 0.0675f) - 4.0f, 3.0f) / 100000.0f;

            // Longer maps are worth more
            speedValue *= 0.95f + 0.4f * Math.Min(1.0f, totalHits / 2000.0f) +
                (totalHits > 2000 ? Math.Log10(totalHits / 2000.0f) * 0.5f : 0.0f);

            // Penalize misses exponentially. This mainly fixes tag4 maps and the likes until a per-hitobject solution is available
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    speedValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.SpeedDifficultStrainCount);
                else
                    speedValue *= Math.Pow(0.97f, effectiveMissCount);
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                speedValue *= Math.Min(Math.Pow(scoreMaxCombo, 0.8f) / Math.Pow(attributes.MaxCombo, 0.8f), 1.0f);

            // Scale the speed value with accuracy _slightly_
            speedValue *= 0.5f + accuracy / 2.0f;
            // It is important to also consider accuracy difficulty when doing that
            speedValue *= 0.98f + Math.Pow(overallDifficulty, 2) / 2500;

            return speedValue;
        }

        private double computeAccuracyValue()
        {
            // This percentage only considers HitCircles of any value - in this part of the calculation we focus on hitting the timing hit window
            double betterAccuracyPercentage;
            int amountHitObjectsWithAccuracy = attributes.HitCircleCount;
            if (enableLazerAcc && !usingClassicSliderAccuracy)
                amountHitObjectsWithAccuracy += attributes.SliderCount;

            if (amountHitObjectsWithAccuracy > 0)
                betterAccuracyPercentage = ((countGreat - (totalHits - amountHitObjectsWithAccuracy)) * 6 + countOk * 2 + countMeh) / (double)(amountHitObjectsWithAccuracy * 6);
            else
                betterAccuracyPercentage = 0;

            // It is possible to reach a negative accuracy with this formula. Cap it at zero - zero points
            if (betterAccuracyPercentage < 0)
                betterAccuracyPercentage = 0;

            // Lots of arbitrary values from testing.
            // Considering to use derivation from perfect accuracy in a probabilistic manner - assume normal distribution
            double accuracyValue = Math.Pow(1.52163f, overallDifficulty) * Math.Pow(betterAccuracyPercentage, 24) * 2.83f;

            // Bonus for many hitcircles - it's harder to keep good accuracy up for longer
            accuracyValue *= Math.Min(1.15f, Math.Pow(amountHitObjectsWithAccuracy / 1000.0f, 0.3f));

            if (mods.Any(m => m is OsuModHidden))
                accuracyValue *= 1.02f;
            if (mods.Any(m => m is OsuModFlashlight))
                accuracyValue *= 1.02f;

            return accuracyValue;
        }

        private double calculateCSRMissPenalty(double missCount, double difficultStrainCount) => 0.96 / ((missCount / (4 * Math.Pow(Math.Log(difficultStrainCount), 0.94))) + 1);
        private int totalHits => countGreat + countOk + countMeh + countMiss;
    }
}
