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

            if (removeRelaxAutopilotPp && score.Mods.Any(m => m is OsuModRelax || m is OsuModAutopilot))
                return new OsuPerformanceAttributes
                {
                    EffectiveMissCount = countMiss
                };

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
            double multiplier = 1.12; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things

            if (mods.Any(m => m is OsuModNoFail))
                multiplier *= Math.Max(0.90, 1.0 - 0.02 * countMiss);

            if (mods.Any(m => m is OsuModSpunOut))
                multiplier *= 1.0 - DiffUtils.Pow((double)this.attributes.SpinnerCount / totalHits, 0.85);

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

            double aimValue = DiffUtils.Pow(5.0 * Math.Max(1.0, rawAim / 0.0675) - 4.0, 3.0) / 100000.0;

            // Longer maps are worth more
            double lengthBonus = 0.95 + 0.4 * Math.Min(1.0, totalHits / 2000.0) +
                                 (totalHits > 2000 ? Math.Log10(totalHits / 2000.0) * 0.5 : 0.0);

            aimValue *= lengthBonus;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    aimValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.AimDifficultStrainCount, attributes.AimTopWeightedSliderFactor, attributes);
                else
                    aimValue *= 0.97 * DiffUtils.Pow(1 - DiffUtils.Pow(effectiveMissCount / totalHits, 0.775), effectiveMissCount);
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                aimValue *= Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8) / DiffUtils.Pow(attributes.MaxCombo, 0.8), 1.0);

            double approachRateFactor = 0.0;
            if (approachRate > 10.33)
                approachRateFactor = approachRate - 10.33;
            else if (approachRate < 8.0)
                approachRateFactor = 0.025 * (8.0 - approachRate);

            double approachRateTotalHitsFactor = 1.0 / (1.0 + Math.Exp(-(0.007 * (totalHits - 400))));

            double approachRateBonus = 1.0 + (0.03 + 0.37 * approachRateTotalHitsFactor) * approachRateFactor;

            // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
            if (mods.Any(h => h is OsuModHidden))
                aimValue *= 1.0 + 0.04 * (12.0 - approachRate);

            double flashlightBonus = 1.0;

            if (mods.Any(h => h is OsuModFlashlight))
            {
                // Add combo scaling for FL if CSR is enabled
                double comboScale = attributes.MaxCombo > 0 ? Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8) / DiffUtils.Pow(attributes.MaxCombo, 0.8), 1.0) : 0;
                double csrAdjust = enableCSR ? comboScale : 1.0;

                // Apply object-based bonus for flashlight.
                flashlightBonus = 1.0 + 0.35 * csrAdjust * Math.Min(1.0, totalHits / 200.0) +
                                  (totalHits > 200
                                      ? 0.3 * Math.Min(1.0, (totalHits - 200) / 300.0) +
                                        (totalHits > 500 ? (totalHits - 500) / 1200.0 : 0.0)
                                      : 0.0);

            }

            aimValue *= Math.Max(flashlightBonus, approachRateBonus);

            // Scale the aim value with accuracy _slightly_
            aimValue *= 0.5 + accuracy / 2.0;
            // It is important to also consider accuracy difficulty when doing that
            aimValue *= 0.98 + DiffUtils.Pow(overallDifficulty, 2) / 2500;

            return aimValue;
        }

        private double computeSpeedValue()
        {
            double speedValue = DiffUtils.Pow(5.0 * Math.Max(1.0, attributes.SpeedDifficulty / 0.0675) - 4.0, 3.0) / 100000.0;

            // Longer maps are worth more
            double lengthBonus = 0.95 + 0.4 * Math.Min(1.0, totalHits / 2000.0) +
                                 (totalHits > 2000 ? Math.Log10(totalHits / 2000.0) * 0.5 : 0.0);
            speedValue *= lengthBonus;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
            {
                if (enableCSR)
                    speedValue *= calculateCSRMissPenalty(effectiveMissCount, attributes.SpeedDifficultStrainCount, attributes.AimTopWeightedSliderFactor, attributes);
                else
                    speedValue *= 0.97 * DiffUtils.Pow(1 - DiffUtils.Pow(effectiveMissCount / totalHits, 0.775), DiffUtils.Pow(effectiveMissCount, .875));
            }

            // Combo scaling
            if (!enableCSR && attributes.MaxCombo > 0)
                speedValue *= Math.Min(DiffUtils.Pow(scoreMaxCombo, 0.8) / DiffUtils.Pow(attributes.MaxCombo, 0.8), 1.0);

            double approachRateFactor = 0.0;
            if (approachRate > 10.33)
                approachRateFactor = approachRate - 10.33;

            double approachRateTotalHitsFactor = 1.0 / (1.0 + Math.Exp(-(0.007 * (totalHits - 400))));

            speedValue *= 1.0 + (0.03 + 0.37 * approachRateTotalHitsFactor) * approachRateFactor;

            if (mods.Any(m => m is OsuModHidden))
                speedValue *= 1.0 + 0.04 * (12.0 - approachRate);

            // Scale the speed value with accuracy and OD
            speedValue *= (0.95 + DiffUtils.Pow(overallDifficulty, 2) / 750) * DiffUtils.Pow(accuracy, (14.5 - Math.Max(overallDifficulty, 8)) / 2);

            // Scale the speed value with # of 50s to punish doubletapping.
            speedValue *= DiffUtils.Pow(0.98, countMeh < totalHits / 500.0 ? 0 : countMeh - totalHits / 500.0);

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
            double accuracyValue = DiffUtils.Pow(1.52163, overallDifficulty) * DiffUtils.Pow(betterAccuracyPercentage, 24) * 2.83;

            // Bonus for many hitcircles - it's harder to keep good accuracy up for longer
            accuracyValue *= Math.Min(1.15, DiffUtils.Pow(amountHitObjectsWithAccuracy / 1000.0, 0.3));

            if (mods.Any(m => m is OsuModHidden))
                accuracyValue *= 1.08;
            if (mods.Any(m => m is OsuModFlashlight))
                accuracyValue *= 1.02;

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
