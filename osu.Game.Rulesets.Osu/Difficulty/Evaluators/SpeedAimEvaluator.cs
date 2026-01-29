// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class SpeedAimEvaluator
    {
        private const double single_spacing_threshold = OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.25; // 1.25 circles distance between centers

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>distance between the previous and current object</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = current.Index > 0 ? (OsuDifficultyHitObject)current.Previous(0) : null;

            double travelDistance = osuPrevObj?.LazyTravelDistance ?? 0;
            double distance = travelDistance + osuCurrObj.LazyJumpDistance;

            // Cap distance at single_spacing_threshold
            distance = Math.Min(distance, single_spacing_threshold);

            // Max distance bonus is 1 * `distance_multiplier` at single_spacing_threshold
            double distanceBonus = Math.Pow(distance / single_spacing_threshold, 3.95);

            // Apply reduced small circle bonus because flow aim difficulty on small circles doesn't scale as hard as jumps
            distanceBonus *= Math.Sqrt(osuCurrObj.SmallCircleBonus);

            double adjustedDistanceScale = 1.0;

            if (osuCurrObj.Angle != null && osuPrevObj?.Angle != null)
            {
                double angleDifference = Math.Abs(osuCurrObj.Angle.Value - osuPrevObj.Angle.Value);
                double angleDifferenceAdjusted = Math.Sin(angleDifference / 2) * 180.0;
                double angularVelocity = angleDifferenceAdjusted / (0.1 * osuCurrObj.AdjustedDeltaTime);
                double angularVelocityBonus = Math.Max(0.0, 0.65 * Math.Log10(angularVelocity));

                // ensure that distance is consistent
                double distancesDifference = 0;
                double distancesAngleDifference = 0;
                int i;
                OsuDifficultyHitObject? currObj = (OsuDifficultyHitObject)current.Previous(0);
                OsuDifficultyHitObject? prevObj = (OsuDifficultyHitObject)current.Previous(1);

                for (i = 0; i < 8; i++)
                {
                    if (currObj != null && prevObj != null)
                    {
                        if (Math.Abs(currObj.DeltaTime - prevObj.DeltaTime) > 25)
                            break;

                        double distanceDifference = Math.Max(Math.Abs(currObj.MinimumJumpDistance - prevObj.MinimumJumpDistance) - 0.5, 0);
                        distancesDifference += distanceDifference;
                        distancesAngleDifference += distanceDifference + 12 * Math.Max(Math.Abs(osuCurrObj.Angle.Value - osuPrevObj.Angle.Value) - 0.1, 0);
                    }
                    else break;

                    currObj = prevObj;
                    prevObj = (OsuDifficultyHitObject)prevObj.Previous(0);
                }

                double averageDistanceAngleDifference = i > 0 ? distancesAngleDifference / i : 0;
                double averageDistanceDifference = i > 0 ? distancesDifference / i : 0;
                double distanceDifferenceScaling = Math.Max(0, 1.0 - averageDistanceDifference / 30.0);
                adjustedDistanceScale = Math.Min(1.0, 0.5 + averageDistanceAngleDifference / 25.0) + angularVelocityBonus * distanceDifferenceScaling;

                double sameRhythmCoef = DifficultyCalculationUtils.Smoothstep(Math.Abs(osuCurrObj.DeltaTime - osuPrevObj.DeltaTime), 15, 30);
                adjustedDistanceScale = double.Lerp(adjustedDistanceScale, 1, sameRhythmCoef);
            }

            distanceBonus *= adjustedDistanceScale;

            double strain = distanceBonus * 1000 / osuCurrObj.AdjustedDeltaTime;

            strain *= highBpmBonus(osuCurrObj.AdjustedDeltaTime);

            return strain;
        }

        private static double highBpmBonus(double ms) => 1 / (1 - Math.Pow(0.3, ms / 1000));
    }
}
