﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuPerformanceCalculator : PerformanceCalculator
    {
        public const double PERFORMANCE_BASE_MULTIPLIER = 1.14; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things.

        private double accuracy;
        private int scoreMaxCombo;
        private int countGreat;
        private int countOk;
        private int countMeh;
        private int countMiss;

        private double effectiveMissCount;

        public OsuPerformanceCalculator()
            : base(new OsuRuleset())
        {
        }

        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        {
            var osuAttributes = (OsuDifficultyAttributes)attributes;

            accuracy = score.Accuracy;
            scoreMaxCombo = score.MaxCombo;
            countGreat = score.Statistics.GetValueOrDefault(HitResult.Great);
            countOk = score.Statistics.GetValueOrDefault(HitResult.Ok);
            countMeh = score.Statistics.GetValueOrDefault(HitResult.Meh);
            countMiss = score.Statistics.GetValueOrDefault(HitResult.Miss);
            effectiveMissCount = calculateEffectiveMissCount(osuAttributes);

            double multiplier = PERFORMANCE_BASE_MULTIPLIER;

            if (score.Mods.Any(m => m is OsuModNoFail))
                multiplier *= Math.Max(0.90, 1.0 - 0.02 * effectiveMissCount);

            if (score.Mods.Any(m => m is OsuModSpunOut) && totalHits > 0)
                multiplier *= 1.0 - Math.Pow((double)osuAttributes.SpinnerCount / totalHits, 0.85);

            if (score.Mods.Any(h => h is OsuModRelax))
            {
                // https://www.desmos.com/calculator/bc9eybdthb
                // we use OD13.3 as maximum since it's the value at which great hitwidow becomes 0
                // this is well beyond currently maximum achievable OD which is 12.17 (DTx2 + DA with OD11)
                double okMultiplier = Math.Max(0.0, osuAttributes.OverallDifficulty > 0.0 ? 1 - Math.Pow(osuAttributes.OverallDifficulty / 13.33, 1.8) : 1.0);
                double mehMultiplier = Math.Max(0.0, osuAttributes.OverallDifficulty > 0.0 ? 1 - Math.Pow(osuAttributes.OverallDifficulty / 13.33, 5) : 1.0);

                // As we're adding Oks and Mehs to an approximated number of combo breaks the result can be higher than total hits in specific scenarios (which breaks some calculations) so we need to clamp it.
                effectiveMissCount = Math.Min(effectiveMissCount + countOk * okMultiplier + countMeh * mehMultiplier, totalHits);
            }

            double power = OsuDifficultyCalculator.SUM_POWER;

            double aimValue = computeAimValue(score, osuAttributes);
            double speedValue = computeSpeedValue(score, osuAttributes);
            double mechanicalValue = Math.Pow(Math.Pow(aimValue, power) + Math.Pow(speedValue, power), 1.0 / power);

            // Cognition

            double lowARValue = computeReadingLowARValue(score, osuAttributes);
            double highARValue = computeReadingHighARValue(score, osuAttributes);

            double readingARValue = Math.Pow(Math.Pow(lowARValue, power) + Math.Pow(highARValue, power), 1.0 / power);

            double flashlightValue = computeFlashlightValue(score, osuAttributes);

            double readingHDValue = 0;
            if (score.Mods.Any(h => h is OsuModHidden))
                readingHDValue = computeReadingHiddenValue(score, osuAttributes);

            // Reduce AR reading bonus if FL is present
            double flPower = OsuDifficultyCalculator.FL_SUM_POWER;
            double flashlightARValue = score.Mods.Any(h => h is OsuModFlashlight) ?
                Math.Pow(Math.Pow(flashlightValue, flPower) + Math.Pow(readingARValue, flPower), 1.0 / flPower) : readingARValue;

            double cognitionValue = flashlightARValue + readingHDValue;
            cognitionValue = AdjustCognitionPerformance(cognitionValue, mechanicalValue, flashlightValue);

            double accuracyValue = computeAccuracyValue(score, osuAttributes);

            // Add cognition value without LP-sum cuz otherwise it makes balancing harder
            double totalValue =
                (Math.Pow(Math.Pow(mechanicalValue, power) + Math.Pow(accuracyValue, power), 1.0 / power)
                + cognitionValue) * multiplier;

            double savedEffectiveMissCount = effectiveMissCount;
            effectiveMissCount = 0;
            countMiss = 0;

            double balanceAdjustingMultiplier = calculateMechanicalBalancingMultiplier(score, osuAttributes);
            totalValue *= balanceAdjustingMultiplier;

            // Fancy stuff for better visual display of FL pp

            // Calculate reading difficulty as there was no FL in the first place
            double visualCognitionValue = AdjustCognitionPerformance(readingARValue + readingHDValue, mechanicalValue, flashlightValue);

            double visualFlashlightValue = cognitionValue - visualCognitionValue;

            return new OsuPerformanceAttributes
            {
                Aim = aimValue,
                Speed = speedValue,
                Accuracy = accuracyValue,
                Flashlight = visualFlashlightValue,
                Reading = visualCognitionValue,
                EffectiveMissCount = effectiveMissCount,
                Total = totalValue
            };
        }

        public static double CalculateDefaultLengthBonus(int objectsCount) => 0.95 + 0.4 * Math.Min(1.0, objectsCount / 2000.0) + (objectsCount > 2000 ? Math.Log10(objectsCount / 2000.0) * 0.5 : 0.0);

        private double computeAimValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double aimValue = OsuStrainSkill.DifficultyToPerformance(attributes.AimDifficulty);

            double lengthBonus = CalculateDefaultLengthBonus(totalHits);
            aimValue *= lengthBonus;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                aimValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), effectiveMissCount);

            aimValue *= getComboScalingFactor(attributes);

            if (score.Mods.Any(m => m is OsuModBlinds))
                aimValue *= 1.3 + (totalHits * (0.0016 / (1 + 2 * effectiveMissCount)) * Math.Pow(accuracy, 16)) * (1 - 0.003 * attributes.DrainRate * attributes.DrainRate);
            else if (score.Mods.Any(m => m is OsuModTraceable))
            {
                // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
                aimValue *= 1.0 + 0.04 * (12.0 - attributes.ApproachRate);
            }

            // We assume 15% of sliders in a map are difficult since there's no way to tell from the performance calculator.
            double estimateDifficultSliders = attributes.SliderCount * 0.15;

            if (attributes.SliderCount > 0)
            {
                double estimateSliderEndsDropped = Math.Clamp(Math.Min(countOk + countMeh + countMiss, attributes.MaxCombo - scoreMaxCombo), 0, estimateDifficultSliders);
                double sliderNerfFactor = (1 - attributes.SliderFactor) * Math.Pow(1 - estimateSliderEndsDropped / estimateDifficultSliders, 3) + attributes.SliderFactor;
                aimValue *= sliderNerfFactor;
            }

            aimValue *= accuracy;
            // It is important to consider accuracy difficulty when scaling with accuracy.
            aimValue *= 0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500;

            return aimValue;
        }

        private double computeSpeedValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (score.Mods.Any(h => h is OsuModRelax))
                return 0.0;

            double speedValue = OsuStrainSkill.DifficultyToPerformance(attributes.SpeedDifficulty);

            double lengthBonus = CalculateDefaultLengthBonus(totalHits);
            speedValue *= lengthBonus;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                speedValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            speedValue *= getComboScalingFactor(attributes);

            if (score.Mods.Any(m => m is OsuModBlinds))
            {
                // Increasing the speed value by object count for Blinds isn't ideal, so the minimum buff is given.
                speedValue *= 1.12;
            }
            else if (score.Mods.Any(m => m is OsuModTraceable))
            {
                // We want to give more reward for lower AR when it comes to aim and HD. This nerfs high AR and buffs lower AR.
                speedValue *= 1.0 + 0.04 * (12.0 - attributes.ApproachRate);
            }

            // Calculate accuracy assuming the worst case scenario
            double relevantTotalDiff = totalHits - attributes.SpeedNoteCount;
            double relevantCountGreat = Math.Max(0, countGreat - relevantTotalDiff);
            double relevantCountOk = Math.Max(0, countOk - Math.Max(0, relevantTotalDiff - countGreat));
            double relevantCountMeh = Math.Max(0, countMeh - Math.Max(0, relevantTotalDiff - countGreat - countOk));
            double relevantAccuracy = attributes.SpeedNoteCount == 0 ? 0 : (relevantCountGreat * 6.0 + relevantCountOk * 2.0 + relevantCountMeh) / (attributes.SpeedNoteCount * 6.0);

            // Scale the speed value with accuracy and OD.
            speedValue *= (0.95 + Math.Pow(attributes.OverallDifficulty, 2) / 750) * Math.Pow((accuracy + relevantAccuracy) / 2.0, (14.5 - Math.Max(attributes.OverallDifficulty, 8)) / 2);

            // Scale the speed value with # of 50s to punish doubletapping.
            speedValue *= Math.Pow(0.99, countMeh < totalHits / 500.0 ? 0 : countMeh - totalHits / 500.0);

            return speedValue;
        }

        private double computeAccuracyValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (score.Mods.Any(h => h is OsuModRelax))
                return 0.0;

            // This percentage only considers HitCircles of any value - in this part of the calculation we focus on hitting the timing hit window.
            double betterAccuracyPercentage;
            int amountHitObjectsWithAccuracy = attributes.HitCircleCount;

            if (amountHitObjectsWithAccuracy > 0)
                betterAccuracyPercentage = ((countGreat - (totalHits - amountHitObjectsWithAccuracy)) * 6 + countOk * 2 + countMeh) / (double)(amountHitObjectsWithAccuracy * 6);
            else
                betterAccuracyPercentage = 0;

            // It is possible to reach a negative accuracy with this formula. Cap it at zero - zero points.
            if (betterAccuracyPercentage < 0)
                betterAccuracyPercentage = 0;

            // Lots of arbitrary values from testing.
            // Considering to use derivation from perfect accuracy in a probabilistic manner - assume normal distribution.
            double accuracyValue = Math.Pow(1.52163, attributes.OverallDifficulty) * Math.Pow(betterAccuracyPercentage, 24) * 2.83;

            // Bonus for many hitcircles - it's harder to keep good accuracy up for longer.
            accuracyValue *= Math.Min(1.15, Math.Pow(amountHitObjectsWithAccuracy / 1000.0, 0.3));

            // Increasing the accuracy value by object count for Blinds isn't ideal, so the minimum buff is given.
            if (score.Mods.Any(m => m is OsuModBlinds))
                accuracyValue *= 1.14;

            if (score.Mods.Any(m => m is OsuModFlashlight))
                accuracyValue *= 1.02;

            // Visual indication bonus
            double visualBonus = 0.1 * logistic(8.0 - attributes.ApproachRate);

            // Buff if OD is way lower than AR
            double ARODDelta = Math.Max(0, attributes.OverallDifficulty - attributes.ApproachRate);

            // This one is goes from 0.0 on delta=0 to 1.0 somewhere around delta=3.4
            double deltaBonus = (1 - Math.Pow(0.95, Math.Pow(ARODDelta, 4)));

            // Nerf delta bonus on OD lower than 10 and 9
            if (attributes.OverallDifficulty < 10)
                deltaBonus *= Math.Pow(attributes.OverallDifficulty / 10, 2);
            if (attributes.OverallDifficulty < 9)
                deltaBonus *= Math.Pow(attributes.OverallDifficulty / 9, 4);

            accuracyValue *= 1 + visualBonus * (1 + 2 * deltaBonus);
            if (score.Mods.Any(h => h is OsuModHidden || h is OsuModTraceable))
                accuracyValue *= 1 + visualBonus * (1 + deltaBonus);

            return accuracyValue;
        }

        private double computeFlashlightValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double flashlightValue = Flashlight.DifficultyToPerformance(attributes.FlashlightDifficulty);

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                flashlightValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            flashlightValue *= getComboScalingFactor(attributes);

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            flashlightValue *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                               (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // Scale the flashlight value with accuracy _slightly_.
            flashlightValue *= 0.5 + accuracy / 2.0;
            // It is important to also consider accuracy difficulty when doing that.
            flashlightValue *= 0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500;

            return flashlightValue;
        }

        private double computeReadingLowARValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double readingValue = ReadingLowAR.DifficultyToPerformance(attributes.ReadingDifficultyLowAR);

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                readingValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            readingValue *= getComboScalingFactor(attributes);

            // Scale the reading value with accuracy _harshly_. Additional note: it would have it's own curve in Statistical Accuracy rework.
            readingValue *= accuracy * accuracy;
            // It is important to also consider accuracy difficulty when doing that.
            readingValue *= Math.Pow(0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500, 2);

            return readingValue;
        }

        private double computeReadingHighARValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            double highARValue = OsuStrainSkill.DifficultyToPerformance(attributes.ReadingDifficultyHighAR);

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                highARValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), effectiveMissCount);

            highARValue *= getComboScalingFactor(attributes);

            // Approximate how much of high AR difficulty is aim
            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(attributes.AimDifficulty);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(attributes.SpeedDifficulty);

            double aimRatio = aimPerformance / (aimPerformance + speedPerformance);

            // Aim part calculation
            double aimPartValue = highARValue * aimRatio;
            {
                // We assume 15% of sliders in a map are difficult since there's no way to tell from the performance calculator.
                double estimateDifficultSliders = attributes.SliderCount * 0.15;

                if (attributes.SliderCount > 0)
                {
                    double estimateSliderEndsDropped = Math.Clamp(Math.Min(countOk + countMeh + countMiss, attributes.MaxCombo - scoreMaxCombo), 0, estimateDifficultSliders);
                    double sliderNerfFactor = (1 - attributes.SliderFactor) * Math.Pow(1 - estimateSliderEndsDropped / estimateDifficultSliders, 3) + attributes.SliderFactor;
                    aimPartValue *= sliderNerfFactor;
                }

                aimPartValue *= accuracy;
                // It is important to consider accuracy difficulty when scaling with accuracy.
                aimPartValue *= 0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500;
            }

            // Speed part calculation
            double speedPartValue = highARValue * (1 - aimRatio);
            {
                // Calculate accuracy assuming the worst case scenario
                double relevantTotalDiff = totalHits - attributes.SpeedNoteCount;
                double relevantCountGreat = Math.Max(0, countGreat - relevantTotalDiff);
                double relevantCountOk = Math.Max(0, countOk - Math.Max(0, relevantTotalDiff - countGreat));
                double relevantCountMeh = Math.Max(0, countMeh - Math.Max(0, relevantTotalDiff - countGreat - countOk));
                double relevantAccuracy = attributes.SpeedNoteCount == 0 ? 0 : (relevantCountGreat * 6.0 + relevantCountOk * 2.0 + relevantCountMeh) / (attributes.SpeedNoteCount * 6.0);

                // Scale the speed value with accuracy and OD.
                speedPartValue *= (0.95 + Math.Pow(attributes.OverallDifficulty, 2) / 750) * Math.Pow((accuracy + relevantAccuracy) / 2.0, (14.5 - Math.Max(attributes.OverallDifficulty, 8)) / 2);

                // Scale the speed value with # of 50s to punish doubletapping.
                speedPartValue *= Math.Pow(0.99, countMeh < totalHits / 500.0 ? 0 : countMeh - totalHits / 500.0);
            }

            return aimPartValue + speedPartValue;
        }

        private double computeReadingHiddenValue(ScoreInfo score, OsuDifficultyAttributes attributes)
        {
            if (!score.Mods.Any(h => h is OsuModHidden))
                return 0.0;

            double hiddenValue = ReadingHidden.DifficultyToPerformance(attributes.HiddenDifficulty);

            double lengthBonus = CalculateDefaultLengthBonus(totalHits);
            hiddenValue *= lengthBonus;

            // Penalize misses by assessing # of misses relative to the total # of objects. Default a 3% reduction for any # of misses.
            if (effectiveMissCount > 0)
                hiddenValue *= 0.97 * Math.Pow(1 - Math.Pow(effectiveMissCount / totalHits, 0.775), Math.Pow(effectiveMissCount, .875));

            hiddenValue *= getComboScalingFactor(attributes);

            // Scale the reading value with accuracy _harshly_. Additional note: it would have it's own curve in Statistical Accuracy rework.
            hiddenValue *= accuracy * accuracy;
            // It is important to also consider accuracy difficulty when doing that.
            hiddenValue *= 0.98 + Math.Pow(attributes.OverallDifficulty, 2) / 2500;

            return hiddenValue;
        }

        private double calculateEffectiveMissCount(OsuDifficultyAttributes attributes)
        {
            // Guess the number of misses + slider breaks from combo
            double comboBasedMissCount = 0.0;

            if (attributes.SliderCount > 0)
            {
                double fullComboThreshold = attributes.MaxCombo - 0.1 * attributes.SliderCount;
                if (scoreMaxCombo < fullComboThreshold)
                    comboBasedMissCount = fullComboThreshold / Math.Max(1.0, scoreMaxCombo);
            }

            // Clamp miss count to maximum amount of possible breaks
            comboBasedMissCount = Math.Min(comboBasedMissCount, countOk + countMeh + countMiss);

            return Math.Max(countMiss, comboBasedMissCount);
        }

        private double calculateMechanicalBalancingMultiplier(ScoreInfo score, OsuDifficultyAttributes osuAttributes)
        {
            scoreMaxCombo = osuAttributes.MaxCombo;

            double multiplier = PERFORMANCE_BASE_MULTIPLIER;
            double power = OsuDifficultyCalculator.SUM_POWER;

            double aimValue = computeAimValue(score, osuAttributes);
            double speedValue = computeSpeedValue(score, osuAttributes);
            double mechanicalValue = Math.Pow(Math.Pow(aimValue, power) + Math.Pow(speedValue, power), 1.0 / power);

            // Cognition

            double lowARValue = computeReadingLowARValue(score, osuAttributes);
            double highARValue = computeReadingHighARValue(score, osuAttributes);

            double readingARValue = Math.Pow(Math.Pow(lowARValue, power) + Math.Pow(highARValue, power), 1.0 / power);

            double flashlightValue = computeFlashlightValue(score, osuAttributes);

            double readingHDValue = 0;
            if (score.Mods.Any(h => h is OsuModHidden))
                readingHDValue = computeReadingHiddenValue(score, osuAttributes);

            // Reduce AR reading bonus if FL is present
            double flPower = OsuDifficultyCalculator.FL_SUM_POWER;
            double flashlightARValue = score.Mods.Any(h => h is OsuModFlashlight) ?
                Math.Pow(Math.Pow(flashlightValue, flPower) + Math.Pow(readingARValue, flPower), 1.0 / flPower) : readingARValue;

            double cognitionValue = flashlightARValue + readingHDValue;
            cognitionValue = AdjustCognitionPerformance(cognitionValue, mechanicalValue, flashlightValue);

            double accuracyValue = computeAccuracyValue(score, osuAttributes);

            // Add cognition value without LP-sum cuz otherwise it makes balancing harder
            double totalValue =
                (Math.Pow(Math.Pow(mechanicalValue, power) + Math.Pow(accuracyValue, power), 1.0 / power)
                + cognitionValue) * multiplier;

            if (totalValue < 1000)
                return 1;

            double rescaledValue = totalValue / 1000 - 1;
            double result = Math.Min(0.1 * Math.Pow(rescaledValue, 0.6), 0.11 * Math.Pow(rescaledValue, 0.3));
            result *= Math.Min(rescaledValue, 0.2) / 0.2; // Smoother curve
            return 1 + result;
        }

        private double getComboScalingFactor(OsuDifficultyAttributes attributes) => attributes.MaxCombo <= 0 ? 1.0 : Math.Min(Math.Pow(scoreMaxCombo, 0.8) / Math.Pow(attributes.MaxCombo, 0.8), 1.0);
        private int totalHits => countGreat + countOk + countMeh + countMiss;

        // Limits reading difficulty by the difficulty of full-memorisation (assumed to be mechanicalPerformance + flashlightPerformance + 25)
        // Desmos graph assuming that x = cognitionPerformance, while y = mechanicalPerformance + flaslightPerformance
        // https://www.desmos.com/3d/vjygrxtkqs
        public static double AdjustCognitionPerformance(double cognitionPerformance, double mechanicalPerformance, double flashlightPerformance)
        {
            // Assuming that less than 25 pp is not worthy for memory
            double capPerformance = mechanicalPerformance + flashlightPerformance + 25;

            double ratio = cognitionPerformance / capPerformance;
            if (ratio > 50) return capPerformance;

            ratio = softmin(ratio * 10, 10, 5) / 10;
            return ratio * capPerformance;
        }

        private static double softmin(double a, double b, double power = Math.E) => a * b / Math.Log(Math.Pow(power, a) + Math.Pow(power, b), power);
        private static double logistic(double x) => 1 / (1 + Math.Exp(-x));
    }
}
