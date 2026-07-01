// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
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

        /// <summary>
        /// Missed slider ticks that includes missed reverse arrows. Will only be correct on non-classic scores
        /// </summary>
        private int countSliderTickMiss;

        /// <summary>
        /// Amount of missed slider tails that don't break combo. Will only be correct on non-classic scores
        /// </summary>
        private int countSliderEndsDropped;

        private double effectiveMissCount;

        private double overallDifficulty;
        private double approachRate;
        private double drainRate;

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
            countSliderEndsDropped = this.attributes.SliderCount - score.Statistics.GetValueOrDefault(HitResult.SliderTailHit);
            countSliderTickMiss = score.Statistics.GetValueOrDefault(HitResult.LargeTickMiss);
            effectiveMissCount = countMiss;

            var difficulty = score.BeatmapInfo!.Difficulty.Clone();

            score.Mods.OfType<IApplicableToDifficulty>().ForEach(m => m.ApplyToDifficulty(difficulty));

            double clockRate = ModUtils.CalculateRateWithMods(score.Mods);

            double greatHitWindow = (int)(80 - 6 * difficulty.OverallDifficulty) / clockRate;
            double preempt = (int)IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, 1800, 1200, 450) / clockRate;

            approachRate = preempt > 1200 ? (1800 - preempt) / 120 : (1200 - preempt) / 150 + 5;
            overallDifficulty = (80 - greatHitWindow) / 6;
            drainRate = difficulty.DrainRate;

            // Return 0 if Relax or Autopilot is used and the setting of ignoring them is enabled
            if (removeRelaxAutopilotPp && score.Mods.Any(m => m is OsuModRelax || m is OsuModAutopilot))
                return new OsuPerformanceAttributes
                {
                    EffectiveMissCount = countMiss
                };

            // If CSR is enabled - we need to estimate sliderbreaks to have correct miss penalties
            if (enableCSR)
            {
                double comboBasedEstimatedMissCount = calculateComboBasedEstimatedMissCount(this.attributes);
                double? scoreBasedEstimatedMissCount = null;

                if (usingClassicSliderAccuracy && score.LegacyTotalScore != null)
                {
                    var legacyScoreMissCalculator = new OsuLegacyScoreMissCalculator(score, this.attributes);
                    scoreBasedEstimatedMissCount = legacyScoreMissCalculator.Calculate();

                    effectiveMissCount = scoreBasedEstimatedMissCount.Value;
                }
                else
                {
                    // Use combo-based miss count if this isn't a legacy score
                    effectiveMissCount = comboBasedEstimatedMissCount;
                }

                effectiveMissCount = Math.Max(countMiss, effectiveMissCount);
                effectiveMissCount = Math.Min(totalHits, effectiveMissCount);
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
                DiffUtils.Pow(
                    DiffUtils.Pow(aimValue, 1.1) +
                    DiffUtils.Pow(speedValue, 1.1) +
                    DiffUtils.Pow(accuracyValue, 1.1), 1.0 / 1.1
                ) * multiplier;

            return new OsuPerformanceAttributes
            {
                Aim = aimValue,
                Speed = speedValue,
                Accuracy = accuracyValue,
                EffectiveMissCount = effectiveMissCount,
                Total = totalValue
            };
        }

        private double computeAimValue()
        {
            double rawAim = attributes.AimDifficulty;

            if (mods.Any(m => m is OsuModTouchDevice))
                rawAim = DiffUtils.Pow(rawAim, 0.8);

            double aimValue = DiffUtils.Pow(5.0f * Math.Max(1.0f, rawAim / 0.0675f) - 4.0f, 3.0f) / 100000.0f;

            // Longer maps are worth more
            double lengthBonus = 0.95f + 0.4f * Math.Min(1.0f, totalHits / 2000.0f) +
                (totalHits > 2000 ? Math.Log10(totalHits / 2000.0f) * 0.5f : 0.0f);

            aimValue *= lengthBonus;

            // Penalize misses exponentially. This mainly fixes tag4 maps and the likes until a per-hitobject solution is available
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    aimValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.AimDifficultStrainCount, attributes.AimTopWeightedSliderFactor, attributes);
                else
                    aimValue *= DiffUtils.Pow(0.97f, effectiveMissCount);
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                aimValue *= Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8f) / DiffUtils.Pow(attributes.MaxCombo, 0.8f), 1.0f);

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

            // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
            if (mods.Any(h => h is OsuModHidden))
                aimValue *= 1.02 + (11.0f - approachRate) / 50.0; // Gives a 1.04 bonus for AR10, a 1.06 bonus for AR9, a 1.02 bonus for AR11.

            if (mods.Any(h => h is OsuModFlashlight))
            {
                // Add combo scaling for FL if CSR is enabled
                double comboScale = attributes.MaxCombo > 0 ? Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8) / DiffUtils.Pow(attributes.MaxCombo, 0.8), 1.0) : 0;
                double csrAdjust = enableCSR ? comboScale : 1.0;

                // Apply length bonus again if flashlight is on simply because it becomes a lot harder on longer maps.
                aimValue *= 1.0f + 0.45f * lengthBonus * csrAdjust;
            }

            // Scale the aim value with accuracy _slightly_
            aimValue *= 0.5f + accuracy / 2.0f;
            // It is important to also consider accuracy difficulty when doing that
            aimValue *= 0.98f + DiffUtils.Pow(overallDifficulty, 2) / 2500;

            return aimValue;
        }

        private double computeSpeedValue()
        {
            double speedValue = DiffUtils.Pow(5.0f * Math.Max(1.0f, attributes.SpeedDifficulty / 0.0675f) - 4.0f, 3.0f) / 100000.0f;

            // Longer maps are worth more
            speedValue *= 0.95f + 0.4f * Math.Min(1.0f, totalHits / 2000.0f) +
                (totalHits > 2000 ? Math.Log10(totalHits / 2000.0f) * 0.5f : 0.0f);

            // Penalize misses exponentially. This mainly fixes tag4 maps and the likes until a per-hitobject solution is available
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    speedValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.SpeedDifficultStrainCount, attributes.AimTopWeightedSliderFactor, attributes);
                else
                    speedValue *= DiffUtils.Pow(0.97f, effectiveMissCount);
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                speedValue *= Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8f) / DiffUtils.Pow(attributes.MaxCombo, 0.8f), 1.0f);

            if (mods.Any(m => m is OsuModHidden))
                speedValue *= 1.18f;

            // Scale the speed value with accuracy _slightly_
            speedValue *= 0.5f + accuracy / 2.0f;
            // It is important to also consider accuracy difficulty when doing that
            speedValue *= 0.98f + DiffUtils.Pow(overallDifficulty, 2) / 2500;

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
                betterAccuracyPercentage = ((countGreat - Math.Max(totalHits - amountHitObjectsWithAccuracy, 0)) * 6 + countOk * 2 + countMeh) / (double)(amountHitObjectsWithAccuracy * 6);
            else
                betterAccuracyPercentage = 0;

            // It is possible to reach a negative accuracy with this formula. Cap it at zero - zero points
            if (betterAccuracyPercentage < 0)
                betterAccuracyPercentage = 0;

            // Lots of arbitrary values from testing.
            // Considering to use derivation from perfect accuracy in a probabilistic manner - assume normal distribution
            double accuracyValue = DiffUtils.Pow(1.52163f, overallDifficulty) * DiffUtils.Pow(betterAccuracyPercentage, 24) * 2.83f;

            // Bonus for many hitcircles - it's harder to keep good accuracy up for longer
            accuracyValue *= Math.Min(1.15f, DiffUtils.Pow(amountHitObjectsWithAccuracy / 1000.0f, 0.3f));

            if (mods.Any(m => m is OsuModHidden))
                accuracyValue *= 1.02f;
            if (mods.Any(m => m is OsuModFlashlight))
                accuracyValue *= 1.02f;

            return accuracyValue;
        }

        private double calculateComboBasedEstimatedMissCount(OsuDifficultyAttributes attributes)
        {
            if (attributes.SliderCount <= 0)
                return countMiss;

            double missCount = countMiss;

            if (usingClassicSliderAccuracy)
            {
                // Consider that full combo is maximum combo minus dropped slider tails since they don't contribute to combo but also don't break it
                // In classic scores we can't know the amount of dropped sliders so we estimate to 10% of all sliders on the map
                double fullComboThreshold = attributes.MaxCombo - 0.1 * attributes.SliderCount;

                if (scoreMaxCombo < fullComboThreshold)
                    missCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);

                // In classic scores there can't be more misses than a sum of all non-perfect judgements
                missCount = Math.Min(missCount, totalImperfectHits);

                // Every slider has *at least* 2 combo attributed in classic mechanics.
                // If they broke on a slider with a tick, then this still works since they would have lost at least 2 combo (the tick and the end)
                // Using this as a max means a score that loses 1 combo on a map can't possibly have been a slider break.
                // It must have been a slider end.
                int maxPossibleSliderBreaks = Math.Min(attributes.SliderCount, (attributes.MaxCombo - scoreMaxCombo) / 2);

                double sliderBreaks = missCount - countMiss;

                if (sliderBreaks > maxPossibleSliderBreaks)
                    missCount = countMiss + maxPossibleSliderBreaks;
            }
            else
            {
                double fullComboThreshold = attributes.MaxCombo - countSliderEndsDropped;

                if (scoreMaxCombo < fullComboThreshold)
                    missCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);

                // Combine regular misses with tick misses since tick misses break combo as well
                missCount = Math.Min(missCount, countSliderTickMiss + countMiss);
            }

            return missCount;
        }

        private double calculateEstimatedSliderBreaks(double topWeightedSliderFactor, OsuDifficultyAttributes attributes)
        {
            if (!usingClassicSliderAccuracy || countOk == 0)
                return 0;

            double missedComboPercent = 1.0 - (double)scoreMaxCombo / attributes.MaxCombo;
            double estimatedSliderBreaks = Math.Min(countOk, effectiveMissCount * topWeightedSliderFactor);

            // Scores with more Oks are more likely to have slider breaks.
            double okAdjustment = ((countOk - estimatedSliderBreaks) + 0.5) / countOk;

            // There is a low probability of extra slider breaks on effective miss counts close to 1, as score based calculations are good at indicating if only a single break occurred.
            estimatedSliderBreaks *= DiffUtils.Smoothstep(effectiveMissCount, 1, 2);

            return estimatedSliderBreaks * okAdjustment * DiffUtils.Logistic(missedComboPercent, 0.33, 15);
        }

        private double calculateCSRMissPenalty(double missCount, double difficultStrainCount, double topWeightedSliderFactor, OsuDifficultyAttributes attributes)
        {
            double estimatedSliderBreaks = calculateEstimatedSliderBreaks(topWeightedSliderFactor, attributes);

            double relevantMissCount = Math.Min(effectiveMissCount + estimatedSliderBreaks, totalImperfectHits + countSliderTickMiss);

            return 0.96 / ((relevantMissCount / (4 * DiffUtils.Pow(Math.Log(difficultStrainCount), 0.94))) + 1);
        }

        private int totalHits => countGreat + countOk + countMeh + countMiss;
        private int totalImperfectHits => countOk + countMeh + countMiss;
    }
}
