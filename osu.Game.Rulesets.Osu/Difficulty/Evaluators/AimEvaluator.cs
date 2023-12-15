// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double wide_angle_multiplier = 1.2;
        private const double acute_angle_multiplier = 1.9;
        private const double slider_multiplier = 1.42;
        private const double snap_velocity_change_multiplier = 0.7;
        private const double flow_velocity_change_multiplier = 1.7;


        private static bool isInvalid(DifficultyHitObject current) => current.Index <= 2 ||
                current.BaseObject is Spinner ||
                current.Previous(0).BaseObject is Spinner ||
                current.Previous(1).BaseObject is Spinner ||
                current.Previous(2).BaseObject is Spinner;

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>angle difficulty,</description></item>
        /// <item><description>sharp velocity increases,</description></item>
        /// <item><description>and slider difficulty.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase)
        {
            if (isInvalid(current))
                return 0;

            (double snap, double flow) difficulties = EvaluateRawDifficultiesOf(current, withSliderTravelDistance, strainDecayBase);
            return EvaluateTotalStrainOf(current, withSliderTravelDistance, strainDecayBase, difficulties);
        }

        public static double GlobalSnapMultiplier => 1;
        public static double GlobalFlowMultiplier => 1;

        public static (double snap, double flow) EvaluateRawDifficultiesOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase)
        {
            if (isInvalid(current))
            {
                return (0, 0);
            }

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);

            //// SNAP ////

            // Calculate the velocity to the current hitobject, which starts with a base distance / time assuming the last object is a hitcircle.
            double currVelocity = getVelocity(osuCurrObj, withSliderTravelDistance);

            // As above, do the same for the previous hitobject.
            double prevVelocity = getVelocity(osuLastObj0, withSliderTravelDistance);

            // Begin angle and weird rewards.
            double minVelocity = Math.Min(currVelocity, prevVelocity);
            double maxVelocity = Math.Max(currVelocity, prevVelocity);

            double lowSpacingBonus = 0;
            double adjustedSnapDistance = osuCurrObj.LazyJumpDistance + lowSpacingBonus;

            // Reduce strain time by 10ms to account for stopping time in wide jumps
            double snapStopTime = 10 * calcWideAngleBonus(osuCurrObj.Angle ?? 0);
            double adjustedStrainTime = Math.Max(osuCurrObj.StrainTime - snapStopTime, 5);

            double adjustedVelocity = adjustedSnapDistance / adjustedStrainTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj0.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj0.TravelDistance / osuLastObj0.TravelTime; // calculate the slider velocity from slider head to slider end.
                double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime; // calculate the movement velocity from slider end to current object

                adjustedVelocity = Math.Max(adjustedVelocity, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            var angleBonus = GetSnapAngleBonus(current, withSliderTravelDistance);

            double wideAngleBonus = angleBonus.wide;
            double acuteAngleBonus = angleBonus.acute;
            double velocityChangeBonus = GetSnapVelocityChangeBonus(current, withSliderTravelDistance);

            double wideVelocityBonus;

            if (currVelocity > prevVelocity)
                wideVelocityBonus = Math.Max(wideAngleBonus, velocityChangeBonus) + Math.Min(wideAngleBonus, velocityChangeBonus) * minVelocity / maxVelocity;
            else
                wideVelocityBonus = wideAngleBonus + velocityChangeBonus;

            // resulting calculation
            double snapDifficulty = adjustedVelocity; // Start strain with velocity.

            // Add in acute angle bonus or wide angle bonus + velocity change bonus, whichever is larger.
            snapDifficulty += Math.Max(acuteAngleBonus, wideVelocityBonus);

            //// FLOW ////

            double flowDifficulty = currVelocity;
            flowDifficulty *= Math.Min(10, osuCurrObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS / 2);

            // Used to penalize additions if there is a change in the rhythm. Possible place to rework.
            double rhythmRatio = Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);

            if (osuCurrObj.Angle != null && osuLastObj0.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj0.Angle.Value;

                double clampedAngleDifference = Math.Clamp(Math.Abs(lastAngle - currAngle) - Math.PI / 6, 0, Math.PI / 3) * 3 / 2;
                double flowSplineBonus = calcFlowAngleChangeBonus(Math.PI / 4 + clampedAngleDifference);
                flowSplineBonus *= 1 - Math.Clamp(osuLastObj1.StrainTime - osuLastObj0.StrainTime, 0, osuLastObj0.StrainTime) / osuLastObj0.StrainTime;
                flowSplineBonus *= 1 - Math.Clamp(osuLastObj0.StrainTime - osuCurrObj.StrainTime, 0, osuCurrObj.StrainTime) / osuCurrObj.StrainTime;

                double velocityThing = 0;
                if (currVelocity > 0)
                    velocityThing = Math.Min(minVelocity, (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)) / currVelocity;

                double angleChangeBonus = flowSplineBonus * velocityThing;

                double acutnessBonus = 0;
                if (currVelocity > 0)
                    acutnessBonus = calcFlowAcuteBonus(currAngle) * Math.Min(minVelocity, (osuCurrObj.Movement - osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)) / currVelocity;

                acutnessBonus *= 1 - getOverlapness(osuCurrObj, osuLastObj0) * getOverlapness(osuCurrObj, osuLastObj1) * getOverlapness(osuLastObj0, osuLastObj1);

                // We reward for angle changes or the acuteness of the angle, whichever is higher. Possibly a case out there to reward both.
                flowDifficulty *= 1 + Math.Max(angleChangeBonus, acutnessBonus);
            }

            double flowVelocityChangeBonus = 0;

            // minimum velocity change is 10%
            double clampedVelocityChange = Math.Abs(currVelocity - prevVelocity) - minVelocity * 0.1;
            if (clampedVelocityChange > 0 && currVelocity > 0)
                flowVelocityChangeBonus = clampedVelocityChange / currVelocity;

            flowDifficulty *= 1 + flow_velocity_change_multiplier * flowVelocityChangeBonus * rhythmRatio;

            // Apply balancing parameters.
            snapDifficulty *= GlobalSnapMultiplier;
            flowDifficulty *= GlobalFlowMultiplier;

            return (snapDifficulty, flowDifficulty);
        }

        private static double getVelocity(OsuDifficultyHitObject osuCurrObj, bool withSliderTravelDistance)
        {
            if (osuCurrObj == null || osuCurrObj.Index < 1) return 0;

            var osuLastObj0 = (OsuDifficultyHitObject)osuCurrObj.Previous(0);

            double velocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

            if (osuLastObj0.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj0.TravelDistance / osuLastObj0.TravelTime;
                double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime;

                velocity = Math.Max(velocity, movementVelocity + travelVelocity);
            }

            return velocity;
        }

        public static (double wide, double acute) GetSnapAngleBonus(DifficultyHitObject current, bool withSliderTravelDistance)
        {
            if (current == null || current.Index < 2) return (0, 0);

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);

            double currVelocity = getVelocity(osuCurrObj, withSliderTravelDistance);
            double prevVelocity = getVelocity(osuLastObj0, withSliderTravelDistance);

            double wideAngleBonus = 0;
            double acuteAngleBonus = 0;

            if (Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime) < 1.25 * Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime)) // If rhythms are the same.
            {
                if (osuCurrObj.Angle != null && osuLastObj0.Angle != null && osuLastObj1.Angle != null)
                {
                    double currAngle = osuCurrObj.Angle.Value;
                    double lastAngle = osuLastObj0.Angle.Value;
                    double lastLastAngle = osuLastObj1.Angle.Value;

                    // Rewarding angles, take the smaller velocity as base.
                    double angleBonus = Math.Min(currVelocity, prevVelocity);

                    wideAngleBonus = calcWideAngleBonus(currAngle);
                    acuteAngleBonus = calcAcuteAngleBonus(currAngle);

                    if (osuCurrObj.StrainTime > 100) // Only buff deltaTime exceeding 300 bpm 1/2.
                        acuteAngleBonus = 0;
                    else
                    {
                        acuteAngleBonus *= calcAcuteAngleBonus(lastAngle) // Multiply by previous angle, we don't want to buff unless this is a wiggle type pattern.
                                           * Math.Min(angleBonus, 125 / osuCurrObj.StrainTime) // The maximum velocity we buff is equal to 125 / strainTime
                                           * Math.Pow(Math.Sin(Math.PI / 2 * Math.Min(1, (100 - osuCurrObj.StrainTime) / 35)), 2) // scale buff from 150 bpm 1/4 to 200 bpm 1/4
                                           * Math.Pow(Math.Sin(Math.PI / 2 * (Math.Clamp(osuCurrObj.LazyJumpDistance, 50, 100) - 50) / 50), 2); // Buff distance exceeding 50 (radius) up to 100 (diameter).
                    }

                    // Don't need anymore?
                    // wideAngleBonus *= angleBonus * (1 - Math.Min(wideAngleBonus, Math.Pow(calcWideAngleBonus(lastAngle), 3)));

                    // Penalize acute angles if they're repeated, reducing the penalty as the lastLastAngle gets more obtuse.
                    acuteAngleBonus *= 0.5 + 0.5 * (1 - Math.Min(acuteAngleBonus, Math.Pow(calcAcuteAngleBonus(lastLastAngle), 3)));
                }
            }

            wideAngleBonus *= wide_angle_multiplier;
            acuteAngleBonus *= acute_angle_multiplier;

            return (wideAngleBonus, acuteAngleBonus);
        }

        public static double GetSnapVelocityChangeBonus(DifficultyHitObject current, bool withSliderTravelDistance)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);

            double currVelocity = getVelocity(osuCurrObj, withSliderTravelDistance);
            double prevVelocity = getVelocity(osuLastObj0, withSliderTravelDistance);

            double velocityChangeBonus = 0;

            if (Math.Max(prevVelocity, currVelocity) != 0)
            {
                // We want to use the average velocity over the whole object when awarding differences, not the individual jump and slider path velocities.
                prevVelocity = (osuLastObj0.LazyJumpDistance + osuLastObj1.TravelDistance) / osuLastObj0.StrainTime;
                currVelocity = (osuCurrObj.LazyJumpDistance + osuLastObj0.TravelDistance) / osuCurrObj.StrainTime;

                // Scale with ratio of difference compared to 0.5 * max dist.
                double distRatio = Math.Pow(Math.Sin(Math.PI / 2 * Math.Abs(prevVelocity - currVelocity) / Math.Max(prevVelocity, currVelocity)), 2);

                // Reward for % distance up to 125 / strainTime for overlaps where velocity is still changing.
                double overlapVelocityBuff = Math.Min(125 / Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime), Math.Abs(prevVelocity - currVelocity));

                velocityChangeBonus = overlapVelocityBuff * distRatio;

                // Penalize for rhythm changes.
                velocityChangeBonus *= Math.Pow(Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime), 2);
            }

            return velocityChangeBonus * snap_velocity_change_multiplier;
        }

        private static double calcWideAngleBonus(double angle) => Math.Pow(Math.Sin(3.0 / 4 * (Math.Min(5.0 / 6 * Math.PI, Math.Max(Math.PI / 6, angle)) - Math.PI / 6)), 2);

        private static double calcAcuteAngleBonus(double angle) => 1 - calcWideAngleBonus(angle);

        private static double calcFlowAcuteBonus(double angle)
        {
            // this is good, based on real aiming mechanism
            angle = Math.Abs(angle);
            if (angle == Math.PI) return 0;
            return (Math.PI - angle) / Math.Sqrt(2 * (1 - Math.Cos(Math.PI - angle))) - 1;
        }

        private static double calcFlowAngleChangeBonus(double angle)
        {
            // need to change this
            angle = Math.Abs(angle);
            return Math.Pow(Math.Sin((Math.Clamp(angle, Math.PI / 3.0, 3 * Math.PI / 4.0) - Math.PI / 3) * 1.2), 2.0);
        }

        private static double getOverlapness(OsuDifficultyHitObject odho1, OsuDifficultyHitObject odho2)
        {
            OsuHitObject o1 = (OsuHitObject)odho1.BaseObject, o2 = (OsuHitObject)odho2.BaseObject;

            double distance = Vector2.Distance(o1.StackedPosition, o2.StackedPosition);

            if (distance > o1.Radius * 2)
                return 0;
            if (distance < o1.Radius)
                return 1;
            return 1 - Math.Pow((distance - o1.Radius) / o1.Radius, 2);
        }

        public static double EvaluateTotalStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (current.Index <= 2 ||
                current.BaseObject is Spinner ||
                current.Previous(0).BaseObject is Spinner ||
                current.Previous(1).BaseObject is Spinner ||
                current.Previous(2).BaseObject is Spinner)
                return 0;

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            return applyRemainingBonusesTo(current, withSliderTravelDistance, strainDecayBase, minStrain);
        }
        public static double EvaluateSnapStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (isInvalid(current))
                return 0;

            if (difficulty.flow < difficulty.snap)
                // make snap difficulty always lower than flow
                difficulty.snap = difficulty.flow * Math.Pow(difficulty.flow / difficulty.snap, 2.0);

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            return applyRemainingBonusesTo(current, false, strainDecayBase, minStrain);
        }

        public static double EvaluateFlowStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (isInvalid(current))
                return 0;

            if (difficulty.snap < difficulty.flow)
            {
                // make flow difficulty always lower than snap
                difficulty.flow = difficulty.snap * Math.Pow(difficulty.snap / difficulty.flow, 2.0);

                // but it can't be lower than snap / 5
                difficulty.flow = Math.Max(difficulty.flow, difficulty.snap / 5);
            }

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            return applyRemainingBonusesTo(current, false, strainDecayBase, minStrain);
        }

        private static double applyRemainingBonusesTo(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, double aimStrain)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);

            double currVelocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

            // Buff cases where the holding of a slider makes the subsequent jump harder, even with leniency abuse.
            // aimStrain = Math.Max(aimStrain, (aimStrain - linearDifficulty * 2.4 * osuCurrObj.Radius / Math.Min(osuCurrObj.MovementTime, osuLastObj0.MovementTime)) * (osuCurrObj.StrainTime / osuCurrObj.MovementTime));

            // Apply small CS buff.
            double smallCSBonus = 1 + Math.Pow(23.04 / ((OsuHitObject)osuCurrObj.BaseObject).Radius, 4.5) / 25; // cs7 have 1.04x multiplier
            aimStrain *= smallCSBonus;

            // Arbitrary cap to bonuses because balancing is hard.
            aimStrain = Math.Min(aimStrain, currVelocity * 3.25);

            // Slider stuff.
            double sliderBonus = 0;
            if (withSliderTravelDistance && osuLastObj0.BaseObject is Slider)
            {
                // Reward sliders based on velocity.
                sliderBonus = osuLastObj0.TravelDistance / osuLastObj0.TravelTime;
            }

            // Add in additional slider velocity bonus.
            if (withSliderTravelDistance)
                aimStrain += sliderBonus * slider_multiplier;

            return aimStrain;
        }
    }
}
