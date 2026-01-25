// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double wide_angle_multiplier = 1.5;
        private const double acute_angle_uncomfy_multiplier = 1.9;
        private const double acute_angle_comfy_multiplier = 0.17;
        private const double slider_multiplier = 1.4;
        private const double velocity_change_multiplier = 0.75;
        private const double wiggle_multiplier = 0.53; // WARNING: Increasing this multiplier beyond 1.02 reduces difficulty as distance increases. Refer to the desmos link above the wiggle bonus calculation

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>angle difficulty,</description></item>
        /// <item><description>sharp velocity increases,</description></item>
        /// <item><description>and slider difficulty.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance)
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);
            var osuLast1Obj = (OsuDifficultyHitObject)current.Previous(1);
            var osuLast2Obj = (OsuDifficultyHitObject)current.Previous(2);

            const int radius = OsuDifficultyHitObject.NORMALISED_RADIUS;
            const int diameter = OsuDifficultyHitObject.NORMALISED_DIAMETER;

            // Start from snapping difficulty
            double currDistance = calculateSnappingDifficulty(osuCurrObj.LazyJumpDistance, osuCurrObj, osuLastObj);

            // Add the distance to current object and find the velocity
            currDistance += osuCurrObj.LazyJumpDistance;
            double currVelocity = currDistance / osuCurrObj.AdjustedDeltaTime;
            double sliderlessCurrVelocity = currVelocity;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj.TravelDistance / osuLastObj.TravelTime; // calculate the slider velocity from slider head to slider end.

                // Need to consider snapping difficulty here as well
                double movement = calculateSnappingDifficulty(osuCurrObj.MinimumJumpDistance, osuCurrObj, osuLastObj);

                movement += osuCurrObj.MinimumJumpDistance;
                double movementVelocity = movement / osuCurrObj.MinimumJumpTime; // calculate the movement velocity from slider end to current object

                currVelocity = Math.Max(currVelocity, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            // In previous velocity calculation accounting for snapping difficulty is not needed, as it's not used as a difficulty base.
            double prevVelocity = osuLastObj.LazyJumpDistance / osuLastObj.AdjustedDeltaTime;

            if (osuLast1Obj.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLast1Obj.TravelDistance / osuLast1Obj.TravelTime;
                double movementVelocity = osuLastObj.MinimumJumpDistance / osuLastObj.MinimumJumpTime;

                prevVelocity = Math.Max(prevVelocity, movementVelocity + travelVelocity);
            }

            double wideAngleBonus = 0;
            double acuteAngleBonus = 0;
            double velocityChangeBonus = 0;
            double wiggleBonus = 0;

            double aimStrain = currVelocity; // Start strain with regular velocity.

            if (osuCurrObj.Angle != null && osuLastObj.Angle != null)
            {
                // Buff wide only if rhythms are the same
                double maxAdjustedDeltaTime = Math.Max(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime);
                double minAdjustedDeltaTime = Math.Min(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime);
                double differentRhythmMultiplier = DifficultyCalculationUtils.Smoothstep(maxAdjustedDeltaTime, 1.25 * minAdjustedDeltaTime, minAdjustedDeltaTime);

                // Buff high bpm and wiggles only if rhythm is the same or getting slower (burst -> jump)
                double fasterRhythmMultiplier = DifficultyCalculationUtils.Smoothstep(osuLastObj.AdjustedDeltaTime, osuCurrObj.AdjustedDeltaTime * 1.25, osuCurrObj.AdjustedDeltaTime);

                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj.Angle.Value;
                double last1Angle = osuLast1Obj.Angle ?? Math.PI;

                // Rewarding angles, take the smaller velocity as base.
                double acuteVelocityBase = Math.Min(currVelocity, prevVelocity);

                // If previous object was a burst - use current velocity instead of min
                acuteVelocityBase = double.Lerp(acuteVelocityBase, currVelocity,
                    Math.Pow(DifficultyCalculationUtils.Smoothstep(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime * 1.75), 2));

                double wideVelocityBase = Math.Min(sliderlessCurrVelocity, prevVelocity); // Don't reward wide angle bonus to sliders

                // Rescale wide angle bonus to reward lower spacing more
                double velocityThreshold = diameter * 2.3 / osuCurrObj.AdjustedDeltaTime;
                wideVelocityBase = Math.Min(wideVelocityBase, velocityThreshold + 0.4 * (wideVelocityBase - velocityThreshold));

                // Potentially wide should also use fasterRhythmMultiplier, but there are some unwanted buffs alongside the wanted ones
                wideAngleBonus = differentRhythmMultiplier * CalcWideAngleBonus(currAngle);
                acuteAngleBonus = fasterRhythmMultiplier * CalcAcuteAngleBonus(currAngle);

                // Penalize angle repetition. Ideally this thing should be removed, but it breaks balance so I've just make it weaker by taking min between angles
                double wideAngleRepetitionNerf = Math.Min(wideAngleBonus, Math.Pow(CalcWideAngleBonus(Math.Min(lastAngle, last1Angle)), 3));
                wideAngleBonus *= 1 - wideAngleRepetitionNerf;

                // We're taking uncomfy as a base here
                double comfyAdjustRatio = acute_angle_comfy_multiplier / acute_angle_uncomfy_multiplier;
                double uncomfyAdjustRatio = (acute_angle_uncomfy_multiplier - acute_angle_comfy_multiplier) / acute_angle_uncomfy_multiplier;

                // Penalize angle repetition.
                acuteAngleBonus *= comfyAdjustRatio + uncomfyAdjustRatio * (1 - Math.Min(acuteAngleBonus, Math.Pow(CalcAcuteAngleBonus(lastAngle), 3)));

                // Apply full wide angle bonus for distance more than one diameter
                wideAngleBonus *= wideVelocityBase * DifficultyCalculationUtils.Smootherstep(osuCurrObj.LazyJumpDistance, 0, diameter);

                // Apply acute angle bonus for BPM above 300 1/2 and distance more than one diameter
                acuteAngleBonus *= acuteVelocityBase *
                                    DifficultyCalculationUtils.Smootherstep(DifficultyCalculationUtils.MillisecondsToBPM(osuCurrObj.AdjustedDeltaTime, 2), 300, 400);

                // Apply wiggle bonus for jumps that are [radius, 3*diameter] in distance
                // https://www.desmos.com/calculator/dp0v0nvowc
                wiggleBonus = acuteVelocityBase
                                * Math.Pow(DifficultyCalculationUtils.ReverseLerp(currDistance, diameter * 3, diameter), 1.8)
                                * Math.Pow(DifficultyCalculationUtils.ReverseLerp(osuLastObj.LazyJumpDistance, diameter * 3, diameter), 1.8);

                if (osuLast2Obj != null)
                {
                    // If objects just go back and forth through a middle point - don't give as much wide bonus
                    // Use Previous(2) and Previous(0) because angles calculation is done prevprev-prev-curr, so any object's angle's center point is always the previous object
                    var lastBaseObject = (OsuHitObject)osuLastObj.BaseObject;
                    var last2BaseObject = (OsuHitObject)osuLast2Obj.BaseObject;

                    float distance = (last2BaseObject.StackedPosition - lastBaseObject.StackedPosition).Length;

                    if (distance < 1)
                    {
                        wideAngleBonus *= 1 - 0.35 * (1 - distance);
                    }
                }
            }

            // We want to use the average velocity over the whole object when awarding differences, not the individual jump and slider path velocities.
            prevVelocity = (osuLastObj.LazyJumpDistance + osuLast1Obj.TravelDistance) / osuLastObj.AdjustedDeltaTime;
            currVelocity = (osuCurrObj.LazyJumpDistance + osuLastObj.TravelDistance) / osuCurrObj.AdjustedDeltaTime;

            if (Math.Max(prevVelocity, currVelocity) != 0)
            {
                // Scale with ratio of difference compared to 0.5 * max dist.
                double distRatio = DifficultyCalculationUtils.Smoothstep(Math.Abs(prevVelocity - currVelocity) / Math.Max(prevVelocity, currVelocity), 0, 1);

                // Reward for % distance up to 125 / AdjustedDeltaTime for overlaps where velocity is still changing.
                double overlapVelocityBuff = Math.Min(diameter * 1.25 / Math.Min(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime), Math.Abs(prevVelocity - currVelocity));

                velocityChangeBonus = overlapVelocityBuff * distRatio;

                // Penalize for rhythm changes.
                double rhythmPenalty = Math.Pow(Math.Min(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime) / Math.Max(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime), 2);
                velocityChangeBonus *= rhythmPenalty;

                double prev1Distance = osuLast1Obj.LazyJumpDistance;
                double prev2Distance = osuLast2Obj?.LazyJumpDistance ?? 0;

                // If previously there was slow flow pattern - sudden velocity change is much easier because you could flow faster to give yourself more time
                // Add radius to account for distance potenitally being very small
                double distanceSimilarityFactor = DifficultyCalculationUtils.ReverseLerp(prev1Distance + radius, (prev2Distance + radius) * 0.8, (prev2Distance + radius) * 0.95);
                double distanceFactor = 0.5 + 0.5 * DifficultyCalculationUtils.ReverseLerp(Math.Max(prev1Distance, prev2Distance), diameter * 1.5, diameter * 0.75);

                // We don't nerf more snappy patterns with this as it's much more difficult to snap faster compared to flow faster
                double angleFactor = DifficultyCalculationUtils.Smoothstep(Math.Max(osuLast1Obj.Angle ?? 0, osuLast2Obj?.Angle ?? 0), Math.PI * 0.55, Math.PI * 0.75);

                velocityChangeBonus *= 1 - distanceSimilarityFactor * distanceFactor * angleFactor * rhythmPenalty;

                // Decrease buff large jumps leading into very small jumps to compensate the fact that smaller jumps are buffed by minimal snap distance
                // Use 2 different curves for doubles and microjumps here for better balancing
                double doublesNerf = DifficultyCalculationUtils.ReverseLerp(osuCurrObj.LazyJumpDistance, diameter, diameter * 3) * DifficultyCalculationUtils.ReverseLerp(osuLastObj.LazyJumpDistance, diameter, radius);
                double microJumpsNerf = DifficultyCalculationUtils.ReverseLerp(osuCurrObj.LazyJumpDistance, diameter * 2.5, diameter * 5) * DifficultyCalculationUtils.ReverseLerp(osuLastObj.LazyJumpDistance, diameter * 2, diameter);
                velocityChangeBonus *= 1 - Math.Max(doublesNerf, microJumpsNerf) * Math.Min(1, rhythmPenalty * 1.05);
            }

            aimStrain += wiggleBonus * wiggle_multiplier;
            aimStrain += velocityChangeBonus * velocity_change_multiplier;

            // Add in acute angle bonus or wide angle bonus, whichever is larger.
            aimStrain += Math.Max(acuteAngleBonus * acute_angle_uncomfy_multiplier, wideAngleBonus * wide_angle_multiplier);

            // Apply high circle size bonus
            aimStrain *= osuCurrObj.SmallCircleBonus;
            aimStrain *= highBpmBonus(osuCurrObj.AdjustedDeltaTime);

            // Add in additional slider velocity bonus.
            if (withSliderTravelDistance)
                aimStrain += CalculateSliderBonus(osuCurrObj);

            return aimStrain;
        }

        public static double CalculateSliderBonus(OsuDifficultyHitObject osuCurrObj)
        {
            if (osuCurrObj.BaseObject is Slider)
            {
                // Reward sliders based on velocity.
                double sliderBonus = osuCurrObj.TravelDistance / osuCurrObj.TravelTime;

                // Add high bpm bonus
                sliderBonus *= highBpmBonus(osuCurrObj.AdjustedDeltaTime);

                return sliderBonus * slider_multiplier;
            }

            return 0;
        }

        private static double calculateSnappingDifficulty(double currDistance, OsuDifficultyHitObject osuCurrObj, OsuDifficultyHitObject osuLastObj)
        {
            const int diameter = OsuDifficultyHitObject.NORMALISED_DIAMETER;

            // Additional reward for wide angles being hard to snap on high BPM
            double angleSnapDifficultyBonus = 0;
            double deltaTimeThreshold = DifficultyCalculationUtils.BPMToMilliseconds(180, 2);

            if (osuCurrObj.AdjustedDeltaTime < deltaTimeThreshold)
            {
                double bpmFactor = Math.Pow((deltaTimeThreshold - osuCurrObj.AdjustedDeltaTime) * 0.015, 2.5);

                angleSnapDifficultyBonus = diameter * bpmFactor;

                // We want to start reward from 60 degrees to 90 degrees on lower spacing, and form 90 degrees to 120 degrees on higher spacing
                double highSpacingAdjust = Math.PI / 3;
                highSpacingAdjust *= DifficultyCalculationUtils.ReverseLerp(currDistance, diameter * 1.5, diameter * 3);

                angleSnapDifficultyBonus *= DifficultyCalculationUtils.Smoothstep(osuCurrObj.Angle ?? 0, Math.PI / 3 + highSpacingAdjust, Math.PI / 2 + highSpacingAdjust);

                // We need to nerf angle snap from both sides - bigger and smaller, as not snapping means angle doesn't matter
                angleSnapDifficultyBonus *= calculateDoublesMultiplier(osuCurrObj, osuLastObj);
                angleSnapDifficultyBonus *= calculateDoublesMultiplier(osuLastObj, osuCurrObj);
            }

            double bpm = DifficultyCalculationUtils.BPMToMilliseconds(osuCurrObj.AdjustedDeltaTime, 2);
            double snapThreshold = diameter * (1 + 1.3 * DifficultyCalculationUtils.ReverseLerp(bpm, 200, 250));

            // Jumps need to have some spacing to be snapped
            double distanceSnapDifficultyBonus = currDistance < snapThreshold ? (snapThreshold * 0.65 + currDistance * 0.35) - currDistance : 0;

            // Only nerf distance for the double itself, not the big jump
            distanceSnapDifficultyBonus *= calculateDoublesMultiplier(osuCurrObj, osuLastObj);

            return distanceSnapDifficultyBonus + angleSnapDifficultyBonus;
        }

        private static double calculateDoublesMultiplier(OsuDifficultyHitObject smallDistanceObj, OsuDifficultyHitObject biggerDistanceObj)
        {
            const int radius = OsuDifficultyHitObject.NORMALISED_RADIUS;
            const int diameter = OsuDifficultyHitObject.NORMALISED_DIAMETER;

            // Don't buff doubles jumps as you don't snap in this case (except very close to itself doubles, that need to have some distance bonus to be calculated as flow)
            double lowSpacingFactor = DifficultyCalculationUtils.ReverseLerp(smallDistanceObj.LazyJumpDistance, diameter * 2, radius);

            // Don't increase snap distance when previous jump is very big, as it leads to cheese being overrewarded
            double bigDistanceDifferenceFactor = DifficultyCalculationUtils.ReverseLerp(biggerDistanceObj.LazyJumpDistance, smallDistanceObj.LazyJumpDistance * 2 + diameter, smallDistanceObj.LazyJumpDistance * 2 + diameter * 2);

            // And don't nerf bursts with this
            bigDistanceDifferenceFactor *= DifficultyCalculationUtils.ReverseLerpTwoDirectional(smallDistanceObj.AdjustedDeltaTime, biggerDistanceObj.AdjustedDeltaTime, 1.95, 1.5);

            return (1 - bigDistanceDifferenceFactor * lowSpacingFactor);
        }

        private static double highBpmBonus(double ms) => 1 / (1 - Math.Pow(0.15, ms / 1000));

        public static double CalcWideAngleBonus(double angle) => DifficultyCalculationUtils.Smoothstep(angle, double.DegreesToRadians(40), double.DegreesToRadians(140));
        public static double CalcAcuteAngleBonus(double angle) => DifficultyCalculationUtils.Smoothstep(angle, double.DegreesToRadians(140), double.DegreesToRadians(40));
    }
}
