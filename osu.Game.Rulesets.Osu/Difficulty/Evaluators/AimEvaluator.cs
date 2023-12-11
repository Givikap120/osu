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
        private const double acute_angle_multiplier = 1.95;
        private const double slider_multiplier = 1.4;
        private const double velocity_change_multiplier = 0.75;

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

        public static double OldSnapAlgo(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase)
        {
            if (isInvalid(current))
            {
                return 0;
            }

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);

            double currVelocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

            //// SNAP ////

            // Velocity stuff
            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj0.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj0.TravelDistance / osuLastObj0.TravelTime; // calculate the slider velocity from slider head to slider end.
                double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime; // calculate the movement velocity from slider end to current object

                currVelocity = Math.Max(currVelocity, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            // As above, do the same for the previous hitobject.
            double prevVelocity = osuLastObj0.LazyJumpDistance / osuLastObj0.StrainTime;

            if (osuLastObj1.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj1.TravelDistance / osuLastObj1.TravelTime;
                double movementVelocity = osuLastObj0.MinimumJumpDistance / osuLastObj0.MinimumJumpTime;

                prevVelocity = Math.Max(prevVelocity, movementVelocity + travelVelocity);
            }

            // in circle radius
            const double low_spacing_multiplier = 0.7;

            const double min_jump = 5;
            const double exp_base = 1.8;

            const double high_bpm_power = 2.8;
            const double low_bpm_power = 2.6;

            const double bpm_point = 100; // in strainTime ms

            // agility bonus
            double normalisedDistance = osuCurrObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS;

            // start from min jump bonus
            double lowSpacingBonusMultiplier = min_jump;

            // different scaling for fast and slow jumps to not overnerf low bpm or high bpm
            if (osuCurrObj.StrainTime < bpm_point)
                lowSpacingBonusMultiplier *= Math.Pow(bpm_point / osuCurrObj.StrainTime, high_bpm_power);
            else
                lowSpacingBonusMultiplier *= Math.Pow(bpm_point / osuCurrObj.StrainTime, low_bpm_power);

            // extremum point to prevent situations where increasing difficulty leads to pp decrease
            double extremumPoint = -Math.Log(1 / (lowSpacingBonusMultiplier * Math.Log(exp_base))) / Math.Log(exp_base);

            // low spacing high bpm bonus
            double lowSpacingBonus;
            if (normalisedDistance < extremumPoint)
                lowSpacingBonus = Math.Pow(exp_base, -extremumPoint) * lowSpacingBonusMultiplier - normalisedDistance + extremumPoint;
            else
                lowSpacingBonus = Math.Pow(exp_base, -normalisedDistance) * lowSpacingBonusMultiplier;

            lowSpacingBonus *= low_spacing_multiplier;
            // lowSpacingBonus += 1; // apply arbitrary xexxar buff cuz otherwise it's ruins all balance

            double adjustedSnapDistance = osuCurrObj.LazyJumpDistance + lowSpacingBonus;

            // Reduce strain time by 20ms to account for stopping time, +10 additional if wide angles
            double snapStopTime = 20 + 10 * calculateAngleSpline(osuCurrObj.Angle ?? 0, false);
            double adjustedStrainTime = Math.Max(osuCurrObj.StrainTime - snapStopTime, 5);

            // snap difficulty
            double snapDifficulty = adjustedSnapDistance / adjustedStrainTime;

            if (osuLastObj0.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj0.TravelDistance / osuLastObj0.TravelTime; // calculate the slider velocity from slider head to slider end.

                double adjustedMinimumJumpTime = Math.Max(osuCurrObj.MinimumJumpTime - snapStopTime, 5);
                double movementVelocity = osuCurrObj.MinimumJumpDistance / adjustedMinimumJumpTime; // calculate the movement velocity from slider end to current object

                snapDifficulty = Math.Max(snapDifficulty, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            // Arbitrary buff for high bpm snap because its hard.
            snapDifficulty *= Math.Pow(Math.Max(1, 100 / osuCurrObj.StrainTime), 0.5);

            // Begin angle and weird rewards.
            double minVelocity = Math.Min(currVelocity, prevVelocity);
            double maxVelocity = Math.Max(currVelocity, prevVelocity);

            // Used to penalize additions if there is a change in the rhythm. Possible place to rework.
            double rhythmRatio = Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);

            double snapAngleBuff = 0;
            if (osuCurrObj.Angle != null && osuLastObj0.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj0.Angle.Value;

                // We reward wide angles on snap.
                snapAngleBuff = calculateAngleSpline(currAngle, false) * Math.Min(minVelocity, (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime));

                // nerf repeated wide angles
                snapAngleBuff *= Math.Pow(calculateAngleSpline(lastAngle, true), 2);
            }

            double snapVelocityBuff = Math.Max(0, Math.Min(Math.Abs(currVelocity - prevVelocity) - Math.Min(currVelocity, prevVelocity), minVelocity)) * rhythmRatio;

            //Console.WriteLine($"Object {current.BaseObject.StartTime}, angle buff +{100 * snapAngleBuff / snapDifficulty:0.0}%, velocity buff +{100 * snapVelocityBuff / snapDifficulty:0.0}%");

            if (currVelocity > prevVelocity)
                snapDifficulty += Math.Max(snapAngleBuff, snapVelocityBuff) + Math.Min(snapAngleBuff, snapVelocityBuff) * minVelocity / maxVelocity;
            else
                snapDifficulty += snapAngleBuff + snapVelocityBuff;

            return snapDifficulty;
        }

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
            double currVelocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

            double lowSpacingBonus = 0;
            double adjustedSnapDistance = osuCurrObj.LazyJumpDistance + lowSpacingBonus;

            // Reduce strain time by 20ms to account for stopping time, +10 additional if wide angles
            double snapStopTime = 20 + 10 * calculateAngleSpline(osuCurrObj.Angle ?? 0, false);
            snapStopTime *= 0; // questionable
            double adjustedStrainTime = Math.Max(osuCurrObj.StrainTime - snapStopTime, 5);

            double adjustedVelocity = adjustedSnapDistance / adjustedStrainTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj0.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj0.TravelDistance / osuLastObj0.TravelTime; // calculate the slider velocity from slider head to slider end.
                double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime; // calculate the movement velocity from slider end to current object

                currVelocity = Math.Max(currVelocity, movementVelocity + travelVelocity);
                adjustedVelocity = Math.Max(adjustedVelocity, movementVelocity + travelVelocity); // take the larger total combined velocity.
            }

            // As above, do the same for the previous hitobject.
            double prevVelocity = osuLastObj0.LazyJumpDistance / osuLastObj0.StrainTime;

            if (osuLastObj1.BaseObject is Slider && withSliderTravelDistance)
            {
                double travelVelocity = osuLastObj1.TravelDistance / osuLastObj1.TravelTime;
                double movementVelocity = osuLastObj0.MinimumJumpDistance / osuLastObj0.MinimumJumpTime;

                prevVelocity = Math.Max(prevVelocity, movementVelocity + travelVelocity);
            }

            double wideAngleBonus = 0;
            double acuteAngleBonus = 0;
            double velocityChangeBonus = 0;

            double snapDifficulty = adjustedVelocity; // Start strain with velocity.

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

                    // Penalize wide angles if they're repeated, reducing the penalty as the lastAngle gets more acute.
                    // wideAngleBonus *= angleBonus * (1 - Math.Min(wideAngleBonus, Math.Pow(calcWideAngleBonus(lastAngle), 3)));
                    // Penalize acute angles if they're repeated, reducing the penalty as the lastLastAngle gets more obtuse.
                    acuteAngleBonus *= 0.5 + 0.5 * (1 - Math.Min(acuteAngleBonus, Math.Pow(calcAcuteAngleBonus(lastLastAngle), 3)));
                }
            }

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

            // Add in acute angle bonus or wide angle bonus + velocity change bonus, whichever is larger.
            snapDifficulty += Math.Max(acuteAngleBonus * acute_angle_multiplier, wideAngleBonus * wide_angle_multiplier + velocityChangeBonus * velocity_change_multiplier);

            //// FLOW ////

            double flowDifficulty = currVelocity;
            flowDifficulty *= Math.Min(10, osuCurrObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS / 2);

            // Begin angle and weird rewards.
            double minVelocity = Math.Min(currVelocity, prevVelocity);
            double maxVelocity = Math.Max(currVelocity, prevVelocity);

            // Used to penalize additions if there is a change in the rhythm. Possible place to rework.
            double rhythmRatio = Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);

            if (osuCurrObj.Angle != null && osuLastObj0.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj0.Angle.Value;

                double clampedAngleDifference = Math.Clamp(Math.Abs(lastAngle - currAngle) - Math.PI / 6, 0, Math.PI / 3) * 3 / 2;
                double flowSplineBonus = calculateAngleSpline(Math.PI / 4 + clampedAngleDifference, false);
                flowSplineBonus *= 1 - Math.Clamp(osuLastObj1.StrainTime - osuLastObj0.StrainTime, 0, osuLastObj0.StrainTime) / osuLastObj0.StrainTime;
                flowSplineBonus *= 1 - Math.Clamp(osuLastObj0.StrainTime - osuCurrObj.StrainTime, 0, osuCurrObj.StrainTime) / osuCurrObj.StrainTime;

                double velocityThing = 0;
                if (currVelocity > 0)
                    velocityThing = Math.Min(minVelocity, (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)) / currVelocity;

                double angleChangeBonus = 1 + flowSplineBonus * velocityThing;

                //Console.WriteLine($"Object {current.BaseObject.StartTime}, Bonus = {acutnessBonus:0.00}, ratio = {acutnessBonus / flowDifficulty}");

                // min angle for bonus is PI/12;
                //double adjustedAngle = Math.Min(Math.PI, currAngle + Math.PI / 12);
                double adjustedAngle = currAngle;
                double acutnessBonus = 0;
                if (currVelocity > 0)
                    acutnessBonus = calculateAngleSpline(adjustedAngle, true) * Math.Min(minVelocity, (osuCurrObj.Movement - osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)) / currVelocity;

                acutnessBonus *= 1 - getOverlapness(osuCurrObj, osuLastObj0) * getOverlapness(osuCurrObj, osuLastObj1) * getOverlapness(osuLastObj0, osuLastObj1);
                acutnessBonus += 1;

                // We reward for angle changes or the acuteness of the angle, whichever is higher. Possibly a case out there to reward both.
                flowDifficulty *= Math.Max(angleChangeBonus, acutnessBonus);
            }

            // minimum velocity change is 10%
            double clampedVelocityChange = Math.Max(0, Math.Abs(currVelocity - prevVelocity) - minVelocity * 0.1) / 0.8;
            // 1.5 is a multiplier for velocity change in flow
            flowDifficulty += 1.5 * clampedVelocityChange * rhythmRatio;

            // Apply balancing parameters.
            snapDifficulty *= 0.98;
            flowDifficulty *= 0.98;

            return (snapDifficulty, flowDifficulty);
        }

        private static double calcWideAngleBonus(double angle) => Math.Pow(Math.Sin(3.0 / 4 * (Math.Min(5.0 / 6 * Math.PI, Math.Max(Math.PI / 6, angle)) - Math.PI / 6)), 2);

        private static double calcAcuteAngleBonus(double angle) => 1 - calcWideAngleBonus(angle);

        public static double EvaluateTotalStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (current.Index <= 2 ||
                current.BaseObject is Spinner ||
                current.Previous(0).BaseObject is Spinner ||
                current.Previous(1).BaseObject is Spinner ||
                current.Previous(2).BaseObject is Spinner)
                return 0;

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double arBuff = getHighARBonus(osuCurrObj);
            // WARNING: VERY QUESTIONABLE LENGTH BONUS FOR HIGH AR
            arBuff *= 1 - Math.Pow(2.7, -(osuCurrObj.Index + 150) / 150); // slight worst case for flow
            arBuff = Math.Max(1, arBuff);

            return arBuff * applyRemainingBonusesTo(current, withSliderTravelDistance, strainDecayBase, minStrain);
        }
        public static double EvaluateSnapStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (isInvalid(current))
                return 0;

            if (difficulty.flow < difficulty.snap)
                // make snap difficulty always lower than flow
                difficulty.snap = difficulty.flow * Math.Pow(difficulty.flow / difficulty.snap, 1.0);

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double arBuff = getHighARBonus(osuCurrObj);
            // WARNING: VERY QUESTIONABLE LENGTH BONUS FOR HIGH AR
            arBuff *= 1 - Math.Pow(2.7, -(osuCurrObj.Index + 120) / 120);
            arBuff = Math.Max(1, arBuff);

            return arBuff * applyRemainingBonusesTo(current, false, strainDecayBase, minStrain);
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

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double arBuff = getHighARBonus(osuCurrObj, false);
            // WARNING: VERY QUESTIONABLE LENGTH BONUS FOR HIGH AR
            arBuff *= 1 - Math.Pow(2.7, -(osuCurrObj.Index + 240) / 240); // 2 times more cuz usually flow is streams
            arBuff = Math.Max(1, arBuff);

            return arBuff * applyRemainingBonusesTo(current, false, strainDecayBase, minStrain);
        }

        private static double getHighARBonus(OsuDifficultyHitObject osuCurrObj, bool applyFollowLineAdjust = true)
        {
            //const double max_bonus = 0.3;

            double arBuff = 1.0;

            //// follow lines make high AR easier, so apply nerf if object isn't new combo
            //double adjustedApproachTime = osuCurrObj.ApproachRateTime;
            //if (applyFollowLineAdjust) adjustedApproachTime += Math.Max(0, (osuCurrObj.FollowLineTime - 200) / 25);

            //// we are assuming that 150ms is a complete memory and the bonus will be maximal (1.4) on this
            //if (adjustedApproachTime < 150)
            //    arBuff += max_bonus;

            //// bonus for AR starts from AR10.3, balancing bonus based on high SR cuz we don't have density calculation
            //else if (adjustedApproachTime < 400)
            //{
            //    arBuff += max_bonus * (1 + Math.Cos(Math.PI * 0.4 * (adjustedApproachTime - 150) / 100)) / 2;
            //}

            return arBuff;
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

        // Big spacing buff
        //private static double getAngleMultiplier(DifficultyHitObject current)
        //{
        //    var osuCurrObj = (OsuDifficultyHitObject)current;
        //    var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);

        //    double snapDifficulty = (osuCurrObj.Movement.Length / (osuCurrObj.StrainTime - 20) + (osuCurrObj.Radius * 2) / (Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime) - 20));
        //    double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;
        //    double prevVelocity = osuLastObj0.Movement.Length / osuLastObj0.StrainTime;

        //    double adjustedSnapDifficulty = snapDifficulty;

        //    if (osuCurrObj.Angle != null)
        //    {
        //        double currAngle = osuCurrObj.Angle.Value;

        //        // We reward wide angles on snap.
        //        adjustedSnapDifficulty += linearDifficulty * calculateAngleSpline(currAngle, false) * Math.Min(Math.Min(currVelocity, prevVelocity), (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime));
        //    }

        //    return adjustedSnapDifficulty / snapDifficulty;
        //}
        public static double AdjustStrainDecay(DifficultyHitObject current, double strainDecayBase)
        {
            if (isInvalid(current))
                return strainDecayBase;

            var osuCurrObj = (OsuDifficultyHitObject)current;

            // distance in circles
            double normalisedDistance = 0.5 * osuCurrObj.LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS;
            //normalisedDistance *= getAngleMultiplier(current);

            // random values that work, you can check desmos
            // double targetDistance = normalisedDistance - Math.Log(Math.Pow(2, normalisedDistance) + 2.45, 6) + 0.92; // old variant

            const double basic_k = 0.5, target_bpm_point = 120; //120 = 250bpm
            double adjustedK = basic_k * Math.Pow(target_bpm_point / osuCurrObj.StrainTime, 0.5);
            double pointAddment = 6 * (1 - adjustedK); // min point is 5.5 circles for target_bpm_point

            double targetDistance;

            if (osuCurrObj.StrainTime > target_bpm_point)
            {
                targetDistance = adjustedK * normalisedDistance + pointAddment * Math.Pow(target_bpm_point / osuCurrObj.StrainTime, 0.5);
            }
            else
            {
                // targetDistance = adjustedK * normalisedDistance + pointAddment * Math.Pow(target_bpm_point / osuCurrObj.StrainTime, 0.75);
                targetDistance = normalisedDistance; // no need to buff high bpm anymore
            }

            if (targetDistance >= normalisedDistance) return strainDecayBase;

            // Console.WriteLine($"Object {current.BaseObject.StartTime}, distance {normalisedDistance:0.0}, target {targetDistance:0.0}");

            double velocity = normalisedDistance / osuCurrObj.StrainTime;
            double adjustedStrainTime = targetDistance / velocity;
            double adjustedStrainDecay = Math.Pow(strainDecayBase, adjustedStrainTime / osuCurrObj.StrainTime);

            return adjustedStrainDecay;
        }

        private static double calculateAngleSpline(double angle, bool reversed)
        {
            angle = Math.Abs(angle);
            if (reversed)
            {
                //return 1 - Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 3.0, 5 * Math.PI / 6.0) - Math.PI / 3), 2.0);
                if (angle == Math.PI) return 0;
                return (Math.PI - angle) / Math.Sqrt(2 * (1 - Math.Cos(Math.PI - angle))) - 1;

            }

            return Math.Pow(Math.Sin((Math.Clamp(angle, Math.PI / 3.0, 3 * Math.PI / 4.0) - Math.PI / 3) * 1.2), 2.0);

        }
    }
}
