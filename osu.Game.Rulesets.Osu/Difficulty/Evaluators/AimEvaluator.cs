// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double wide_angle_multiplier = 1.5;
        private const double acute_angle_multiplier = 1.95;
        private const double slider_multiplier = 1.35;
        private const double velocity_change_multiplier = 0.75;

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
            var osuLastLastObj = (OsuDifficultyHitObject)current.Previous(1);

            // Doubletap detection stuff

            static double cosSigmoid(double value, double range = 1) => (1 - Math.Cos(value * Math.PI / range)) / 2;

            // Calculate how much things are overlapping. Low overlapping circles are very hard in being doubletappable
            double overlapnessCurr = Math.Clamp(osuCurrObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS / 2, 0, 1);
            overlapnessCurr = cosSigmoid(overlapnessCurr);
            overlapnessCurr *= overlapnessCurr;

            double antiOverlapnessLast = 1 - Math.Clamp(osuLastObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS / 2, 0, 1);
            antiOverlapnessLast = cosSigmoid(antiOverlapnessLast);
            antiOverlapnessLast *= antiOverlapnessLast;

            // Doubletap hitwindow lands in range [0, 300 hitwindow], where 0 is impossible to doubletap 
            double doubletapHitWindow = (osuLastObj.HitWindowGreat + osuLastLastObj.HitWindowGreat) / 2 - osuLastObj.StrainTime;

            // Extra time to aim when doubletapping
            double doubletapTime;

            if (doubletapHitWindow > 0)
            {
                // Always equal or bigger than doubletapTime from `else`
                doubletapTime = osuLastObj.StrainTime * overlapnessCurr * antiOverlapnessLast / 2;
            }
            else
            {
                doubletapTime = osuLastObj.HitWindowGreat * overlapnessCurr * antiOverlapnessLast / 2;

                // if the second note is delayed - decrease the penalty down to 0 on doubled time
                double difference = osuCurrObj.DeltaTime - osuLastObj.DeltaTime;
                double min = Math.Min(osuCurrObj.DeltaTime, osuLastObj.DeltaTime);
                difference = Math.Clamp(difference, -min, min);

                doubletapTime *= 1 - cosSigmoid(difference, min);
            }

            double adjustedStrainTime = osuCurrObj.StrainTime + doubletapTime;

            // Calculate the velocity to the current hitobject, which starts with a base distance / time assuming the last object is a hitcircle.
            double currVelocity = osuCurrObj.LazyJumpDistance / adjustedStrainTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj.TravelDistance / osuLastObj.TravelTime; // calculate the slider velocity from slider head to slider end.
                double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime; // calculate the movement velocity from slider end to current object

                currVelocity = Math.Max(currVelocity, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            // As above, do the same for the previous hitobject.
            double prevVelocity = osuLastObj.LazyJumpDistance / osuLastObj.StrainTime;

            if (osuLastLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastLastObj.TravelDistance / osuLastLastObj.TravelTime;
                double movementVelocity = osuLastObj.MinimumJumpDistance / osuLastObj.MinimumJumpTime;

                prevVelocity = Math.Max(prevVelocity, movementVelocity + travelVelocity);
            }

            double wideAngleBonus = 0;
            double acuteAngleBonus = 0;
            double sliderBonus = 0;
            double velocityChangeBonus = 0;

            double aimStrain = currVelocity; // Start strain with regular velocity.

            if (Math.Max(adjustedStrainTime, osuLastObj.StrainTime) < 1.25 * Math.Min(adjustedStrainTime, osuLastObj.StrainTime)) // If rhythms are the same.
            {
                if (osuCurrObj.Angle != null && osuLastObj.Angle != null && osuLastLastObj.Angle != null)
                {
                    double currAngle = osuCurrObj.Angle.Value;
                    double lastAngle = osuLastObj.Angle.Value;
                    double lastLastAngle = osuLastLastObj.Angle.Value;

                    // Rewarding angles, take the smaller velocity as base.
                    double angleBonus = Math.Min(currVelocity, prevVelocity);

                    wideAngleBonus = calcWideAngleBonus(currAngle);
                    acuteAngleBonus = calcAcuteAngleBonus(currAngle);

                    if (adjustedStrainTime > 100) // Only buff deltaTime exceeding 300 bpm 1/2.
                        acuteAngleBonus = 0;
                    else
                    {
                        acuteAngleBonus *= calcAcuteAngleBonus(lastAngle) // Multiply by previous angle, we don't want to buff unless this is a wiggle type pattern.
                                           * Math.Min(angleBonus, 125 / adjustedStrainTime) // The maximum velocity we buff is equal to 125 / strainTime
                                           * Math.Pow(Math.Sin(Math.PI / 2 * Math.Min(1, (100 - adjustedStrainTime) / 25)), 2) // scale buff from 150 bpm 1/4 to 200 bpm 1/4
                                           * Math.Pow(Math.Sin(Math.PI / 2 * (Math.Clamp(osuCurrObj.LazyJumpDistance, 50, 100) - 50) / 50), 2); // Buff distance exceeding 50 (radius) up to 100 (diameter).
                    }

                    // Penalize wide angles if they're repeated, reducing the penalty as the lastAngle gets more acute.
                    wideAngleBonus *= angleBonus * (1 - Math.Min(wideAngleBonus, Math.Pow(calcWideAngleBonus(lastAngle), 3)));
                    // Penalize acute angles if they're repeated, reducing the penalty as the lastLastAngle gets more obtuse.
                    acuteAngleBonus *= 0.5 + 0.5 * (1 - Math.Min(acuteAngleBonus, Math.Pow(calcAcuteAngleBonus(lastLastAngle), 3)));
                }
            }

            if (Math.Max(prevVelocity, currVelocity) != 0)
            {
                // We want to use the average velocity over the whole object when awarding differences, not the individual jump and slider path velocities.
                prevVelocity = (osuLastObj.LazyJumpDistance + osuLastLastObj.TravelDistance) / osuLastObj.StrainTime;
                currVelocity = (osuCurrObj.LazyJumpDistance + osuLastObj.TravelDistance) / adjustedStrainTime;

                // Scale with ratio of difference compared to 0.5 * max dist.
                double distRatio = Math.Pow(Math.Sin(Math.PI / 2 * Math.Abs(prevVelocity - currVelocity) / Math.Max(prevVelocity, currVelocity)), 2);

                // Reward for % distance up to 125 / strainTime for overlaps where velocity is still changing.
                double overlapVelocityBuff = Math.Min(125 / Math.Min(adjustedStrainTime, osuLastObj.StrainTime), Math.Abs(prevVelocity - currVelocity));

                velocityChangeBonus = overlapVelocityBuff * distRatio;

                // Penalize for rhythm changes.
                velocityChangeBonus *= Math.Pow(Math.Min(adjustedStrainTime, osuLastObj.StrainTime) / Math.Max(adjustedStrainTime, osuLastObj.StrainTime), 2);
            }

            if (osuLastObj.BaseObject is Slider)
            {
                // Reward sliders based on velocity.
                sliderBonus = osuLastObj.TravelDistance / osuLastObj.TravelTime;
            }

            // Add in acute angle bonus or wide angle bonus + velocity change bonus, whichever is larger.
            aimStrain += Math.Max(acuteAngleBonus * acute_angle_multiplier, wideAngleBonus * wide_angle_multiplier + velocityChangeBonus * velocity_change_multiplier);

            // Add in additional slider velocity bonus.
            if (withSliderTravelDistance)
                aimStrain += sliderBonus * slider_multiplier;

            return aimStrain;
        }

        private static double calcWideAngleBonus(double angle) => Math.Pow(Math.Sin(3.0 / 4 * (Math.Min(5.0 / 6 * Math.PI, Math.Max(Math.PI / 6, angle)) - Math.PI / 6)), 2);

        private static double calcAcuteAngleBonus(double angle) => 1 - calcWideAngleBonus(angle);
    }
}
