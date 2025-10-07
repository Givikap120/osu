// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuRatingCalculator
    {
        private const double difficulty_multiplier = 0.0675;

        private const double relax_multiplier = 0.87;
        private const double touch_device_multiplier = 0.83;

        // Increasing this multiplier buffs versatile aim+flow maps
        public const double AIM_VERSATILITY_BONUS = 0.1;

        private readonly Mod[] mods;
        private readonly int totalHits;
        private readonly double overallDifficulty;

        public OsuRatingCalculator(Mod[] mods, int totalHits, double overallDifficulty)
        {
            this.mods = mods;
            this.totalHits = totalHits;
            this.overallDifficulty = overallDifficulty;
        }
        public double ComputeTotalAimRating(double aimDifficultyValue, double snapAimDifficultyValue, double flowAimDifficultyValue)
        {
            if (mods.Any(m => m is OsuModAutopilot))
                return 0;

            double aimRating = CalculateDifficultyRating(aimDifficultyValue);
            double snapAimRating = CalculateDifficultyRating(snapAimDifficultyValue);
            double flowAimRating = CalculateDifficultyRating(flowAimDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
            {
                aimRating = Math.Pow(aimRating, touch_device_multiplier);
                snapAimRating = Math.Pow(snapAimRating, touch_device_multiplier);
                // no reduce on flow aim rating is intentional
            }

            if (mods.Any(m => m is OsuModRelax))
            {
                aimRating *= relax_multiplier;
                snapAimRating *= relax_multiplier;
                flowAimRating *= relax_multiplier;

                // Remove big chunk of flow aim difficulty
                aimRating = double.Lerp(snapAimRating, aimRating, 0.5);
            }

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                aimRating *= 1.0 - magnetisedStrength;
                snapAimRating *= 1.0 - magnetisedStrength;
                flowAimRating *= 1.0 - magnetisedStrength;
            }

            // We consider that average map has ratio of summed ratings to total to be equal to 1.7x
            double baseVersatilityBonus = double.Lerp(1, 1.7, AIM_VERSATILITY_BONUS);

            aimRating = double.Lerp(aimRating, snapAimRating + flowAimRating, AIM_VERSATILITY_BONUS) / baseVersatilityBonus;

            return computeRawAimRating(aimRating);
        }

        public double ComputeSnapAimRating(double snapAimDifficultyValue)
        {
            if (mods.Any(m => m is OsuModAutopilot))
                return 0;

            double snapAimRating = Math.Sqrt(snapAimDifficultyValue) * difficulty_multiplier;

            if (mods.Any(m => m is OsuModTouchDevice))
                snapAimRating = Math.Pow(snapAimRating, touch_device_multiplier);

            // To ensure that result would not be bigger than normal aim difficulty rating
            if (mods.Any(m => m is OsuModRelax))
                snapAimRating *= relax_multiplier;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                snapAimRating *= 1.0 - magnetisedStrength;
            }

            return computeRawAimRating(snapAimRating);
        }

        public double ComputeFlowAimRating(double flowAimDifficultyValue)
        {
            if (mods.Any(m => m is OsuModAutopilot) || mods.Any(m => m is OsuModRelax))
                return 0;

            double flowAimRating = Math.Sqrt(flowAimDifficultyValue) * difficulty_multiplier;

            // To ensure that result would not be bigger than normal aim difficulty rating
            if (mods.Any(m => m is OsuModTouchDevice))
                flowAimRating = Math.Pow(flowAimRating, 0.83);

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                flowAimRating *= 1.0 - magnetisedStrength;
            }

            return computeRawAimRating(flowAimRating);
        }

        private double computeRawAimRating(double aimRating)
        {
            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                aimRating *= 1.0 - magnetisedStrength;
            }

            double ratingMultiplier = 1.0;

            // It is important to consider accuracy difficulty when scaling with accuracy.
            ratingMultiplier *= 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;

            return aimRating * Math.Cbrt(ratingMultiplier);
        }

        public double ComputeSpeedRating(double speedDifficultyValue)
        {
            if (mods.Any(m => m is OsuModRelax))
                return 0;

            double speedRating = CalculateDifficultyRating(speedDifficultyValue);

            double ratingMultiplier = 1.0;

            ratingMultiplier *= 0.95 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 750;

            return speedRating * Math.Cbrt(ratingMultiplier);
        }

        public double ComputeReadingRating(double readingDifficultyValue)
        {
            double readingRating = CalculateDifficultyRating(readingDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                readingRating = Math.Pow(readingRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                readingRating *= 0.7;
            else if (mods.Any(m => m is OsuModAutopilot))
                readingRating *= 0.4;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                readingRating *= 1.0 - magnetisedStrength;
            }

            double ratingMultiplier = 1.0;

            // It is important to also consider accuracy difficulty when doing that.
            ratingMultiplier *= 0.75 + Math.Pow(Math.Max(0, overallDifficulty), 2.2) / 800;

            return readingRating * Math.Sqrt(ratingMultiplier);
        }

        public double ComputeFlashlightRating(double flashlightDifficultyValue)
        {
            if (!mods.Any(m => m is OsuModFlashlight))
                return 0;

            double flashlightRating = CalculateDifficultyRating(flashlightDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                flashlightRating = Math.Pow(flashlightRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                flashlightRating *= 0.7;
            else if (mods.Any(m => m is OsuModAutopilot))
                flashlightRating *= 0.4;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                flashlightRating *= 1.0 - magnetisedStrength;
            }

            if (mods.Any(m => m is OsuModDeflate))
            {
                float deflateInitialScale = mods.OfType<OsuModDeflate>().First().StartScale.Value;
                flashlightRating *= Math.Clamp(DifficultyCalculationUtils.ReverseLerp(deflateInitialScale, 11, 1), 0.1, 1);
            }

            double ratingMultiplier = 1.0;

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            ratingMultiplier *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                                (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // It is important to consider accuracy difficulty when scaling with accuracy.
            ratingMultiplier *= 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;

            return flashlightRating * Math.Sqrt(ratingMultiplier);
        }

        /// <summary>
        /// Calculates a visibility bonus that is applicable to Hidden and Traceable.
        /// </summary>
        public static double CalculateVisibilityBonus(double approachRate, double visibilityFactor = 1, double sliderFactor = 1)
        {
            // Start from normal curve, rewarding lower AR up to AR7
            double readingBonus = 0.04 * (12.0 - Math.Max(approachRate, 7));

            readingBonus *= visibilityFactor;

            // We want to reward slideraim on low AR less
            double sliderVisibilityFactor = Math.Pow(sliderFactor, 3);

            // For AR up to 0 - reduce reward for very low ARs when object is visible
            if (approachRate < 7)
                readingBonus += 0.03 * (7.0 - Math.Max(approachRate, 0)) * sliderVisibilityFactor;

            // Starting from AR0 - cap values so they won't grow to infinity
            if (approachRate < 0)
                readingBonus += 0.075 * (1 - Math.Pow(1.5, approachRate)) * sliderVisibilityFactor;

            return readingBonus;
        }

        public static double CalculateDifficultyRating(double difficultyValue) => Math.Sqrt(difficultyValue) * difficulty_multiplier;
    }
}
