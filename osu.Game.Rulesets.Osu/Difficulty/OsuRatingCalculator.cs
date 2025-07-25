// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuRatingCalculator
    {
        public const double DIFFICULTY_MULTIPLIER = 0.0675;

        private readonly Mod[] mods;
        private readonly int totalHits;
        private readonly double overallDifficulty;

        public OsuRatingCalculator(Mod[] mods, int totalHits, double overallDifficulty)
        {
            this.mods = mods;
            this.totalHits = totalHits;
            this.overallDifficulty = overallDifficulty;
        }

        public double ComputeAimRating(double aimDifficultyValue)
        {
            if (mods.Any(m => m is OsuModAutopilot))
                return 0;

            double aimRating = CalculateDifficultyRating(aimDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                aimRating = Math.Pow(aimRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                aimRating *= 0.9;

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

            if (mods.Any(m => m is OsuModAutopilot))
                speedRating *= 0.5;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                // reduce speed rating because of the speed distance scaling, with maximum reduction being 0.7x
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                speedRating *= 1.0 - magnetisedStrength * 0.3;
            }

            double ratingMultiplier = 1.0;

            ratingMultiplier *= 0.95 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 750;

            return speedRating * Math.Cbrt(ratingMultiplier);
        }

        public double ComputeFlashlightRating(double flashlightDifficultyValue)
        {
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

            double ratingMultiplier = 1.0;

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            ratingMultiplier *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                                (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // It is important to consider accuracy difficulty when scaling with accuracy.
            ratingMultiplier *= 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;

            return flashlightRating * Math.Sqrt(ratingMultiplier);
        }

        public double ComputeReadingLowArRating(double readingLowArDifficultyValue)
        {
            double lowArRating = CalculateDifficultyRating(readingLowArDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                lowArRating = Math.Pow(lowArRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                lowArRating *= 0.95;
            else if (mods.Any(m => m is OsuModAutopilot))
                lowArRating *= 0.4;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                lowArRating *= 1.0 - 0.6 * magnetisedStrength;
            }

            double ratingMultiplier = Math.Pow(0.98 + Math.Pow(overallDifficulty, 2) / 2500, 2);

            return lowArRating * Math.Pow(ratingMultiplier, 0.25);
        }

        public double ComputeReadingHighArRating(double readingHighArDifficultyValue, double aimRating, double speedRating)
        {
            double highArRating = CalculateDifficultyRating(readingHighArDifficultyValue);

            // Approximate how much of high AR difficulty is aim
            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);

            double aimRatio = aimPerformance / (aimPerformance + speedPerformance);
            double aimRatingMultiplier = 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;
            double speedRatingMultiplier = 0.95 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 750;
            double ratingMultiplier = aimRatingMultiplier * aimRatio + speedRatingMultiplier * (1 - aimRatio);

            double aimRatingRatio = aimRating / (aimRating + speedRating);

            if (mods.Any(m => m is OsuModTouchDevice))
                highArRating = Math.Pow(highArRating, double.Lerp(aimRatingRatio, 1, 0.9));

            if (mods.Any(m => m is OsuModRelax))
                highArRating *= double.Lerp(aimRatingRatio, 0, 0.9);
            else if (mods.Any(m => m is OsuModAutopilot))
                highArRating *= double.Lerp(aimRatingRatio, 0.5, 0);

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                highArRating *= 1.0 - magnetisedStrength * double.Lerp(aimRatingRatio, 0.3, 1);
            }

            return highArRating * Math.Cbrt(ratingMultiplier);
        }

        public double ComputeReadingHiddenRating(double readingHiddenDifficultyValue)
        {
            if (mods.Any(m => m is OsuModAutopilot))
                return 0;

            double hiddenRating = CalculateDifficultyRating(readingHiddenDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                hiddenRating = Math.Pow(hiddenRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                hiddenRating *= 0.7;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                hiddenRating *= 1.0 - magnetisedStrength;
            }

            double ratingMultiplier = 0.98 + Math.Pow(overallDifficulty, 2) / 2500;

            return hiddenRating * Math.Cbrt(ratingMultiplier);
        }

        /// <summary>
        /// Calculates a visibility bonus that is applicable to Hidden and Traceable.
        /// </summary>
        public static double CalculateVisibilityBonus(Mod[] mods, double approachRate, double visibilityFactor = 1)
        {
            // NOTE: TC's effect is only noticeable in performance calculations until lazer mods are accounted for server-side.
            bool isAlwaysPartiallyVisible = mods.OfType<OsuModHidden>().Any(m => m.OnlyFadeApproachCircles.Value) || mods.OfType<OsuModTraceable>().Any();

            // Start from normal curve, rewarding lower AR up to AR5
            double readingBonus = 0.04 * (12.0 - Math.Max(approachRate, 5));

            readingBonus *= visibilityFactor;

            // For AR up to 0 - reduce reward for very low ARs when object is visible
            if (approachRate < 5)
                readingBonus += (isAlwaysPartiallyVisible ? 0.03 : 0.04) * (5.0 - Math.Max(approachRate, 0));

            // Starting from AR0 - cap values so they won't grow to infinity
            if (approachRate < 0)
                readingBonus += (isAlwaysPartiallyVisible ? 0.075 : 0.1) * (1 - Math.Pow(1.5, approachRate));

            return readingBonus;
        }

        public static double CalculateDifficultyRating(double difficultyValue) => Math.Sqrt(difficultyValue) * DIFFICULTY_MULTIPLIER;
    }
}
