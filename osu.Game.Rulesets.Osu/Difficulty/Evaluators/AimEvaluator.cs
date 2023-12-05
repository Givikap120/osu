// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double wide_angle_multiplier = 1.0;
        private const double acute_angle_multiplier = 1.95;
        private const double slider_multiplier = 1.35;
        private const double velocity_change_multiplier = 0.75;
        private const double reaction_time = 150;

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
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, double currentRhythm)
        {
            if (isInvalid(current))
                return 0;

            (double snap, double flow) difficulties = EvaluateRawDifficultiesOf(current, strainDecayBase);
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
        public static (double snap, double flow) EvaluateRawDifficultiesOf(DifficultyHitObject current, double strainDecayBase)
        {
            if (isInvalid(current))
            {
                //Console.WriteLine(calculateAngleSpline(0, false));
                //Console.WriteLine(calculateAngleSpline(Math.PI / 4, false));
                //Console.WriteLine(calculateAngleSpline(Math.PI / 2, false));
                //Console.WriteLine(calculateAngleSpline(3 * Math.PI / 4, false));
                //Console.WriteLine(calculateAngleSpline(Math.PI, false));
                return (0, 0);
            }

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);

            //////////////////////// CIRCLE SIZE /////////////////////////
            double linearDifficulty = 32.0 / osuCurrObj.Radius;

            // Flow Stuff

            double flowDifficulty = linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime;

            flowDifficulty *= Math.Min(10, osuCurrObj.Movement.Length / (osuCurrObj.Radius * 2));

            //double decayDuringDeltaT = Math.Pow(strainDecayBase, current.DeltaTime / 1000);
            //double limitOfVelocity = flowDifficulty0 / (1 - decayDuringDeltaT);
            //double adjustedLimitOfVelocity = flowDifficulty / (1 - decayDuringDeltaT);

            //Console.WriteLine($"Object {current.BaseObject.StartTime}, base velocity {flowDifficulty0:0.00}, adjusted {flowDifficulty:0.00}, limit - {limitOfVelocity:0.00}, adj.limit - {adjustedLimitOfVelocity:0.00}");


            // Snap Stuff
            // in circle radius
            const double low_spacing_multiplier = 0.7;

            const double min_jump = 5;
            const double exp_base = 1.8;

            const double high_bpm_power = 2.8;
            const double low_bpm_power = 2.6;

            const double bpm_point = 100; // in strainTime ms

            // agility bonus
            double normalisedDistance = osuCurrObj.Movement.Length / osuCurrObj.Radius;

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
            if(normalisedDistance < extremumPoint)
                lowSpacingBonus = Math.Pow(exp_base, -extremumPoint) * lowSpacingBonusMultiplier - normalisedDistance + extremumPoint;
            else
                lowSpacingBonus = Math.Pow(exp_base, -normalisedDistance) * lowSpacingBonusMultiplier;

            lowSpacingBonus *= low_spacing_multiplier;
            lowSpacingBonus += 1; // apply arbitrary xexxar buff cuz otherwise it's ruins all balance

            double adjustedSnapDistance = osuCurrObj.Movement.Length + osuCurrObj.Radius * lowSpacingBonus;

            // Reduce strain time by 20ms to account for stopping time, +10 additional if wide angles
            double snapStopTime = 20 + 10 * calculateAngleSpline(osuCurrObj.Angle ?? 0, false);
            double adjustedStrainTime = Math.Max(osuCurrObj.StrainTime - snapStopTime, 5);

            // snap difficulty
            double snapDifficulty = linearDifficulty * (adjustedSnapDistance / adjustedStrainTime);

            //double adjustedSnapRatio = 1;
            //if (osuCurrObj.Movement.Length > 0) // just in case
            //{
            //    adjustedSnapRatio = (adjustedSnapDistance / adjustedStrainTime) / (osuCurrObj.Movement.Length / osuCurrObj.StrainTime);
            //    adjustedSnapRatio = Math.Max(adjustedSnapRatio, 1); // it can't nerf something
            //}

            // Arbitrary buff for high bpm snap because its hard.
            snapDifficulty *= Math.Pow(Math.Max(1, 100 / osuCurrObj.StrainTime), 0.5);

            // Begin angle and weird rewards.
            double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;
            double prevVelocity = osuLastObj0.Movement.Length / osuLastObj0.StrainTime;
            double minVelocity = Math.Min(currVelocity, prevVelocity);
            double maxVelocity = Math.Max(currVelocity, prevVelocity);

            // Used to penalize additions if there is a change in the rhythm. Possible place to rework.
            double rhythmRatio = Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);

            double snapAngleBuff = 0;
            if (osuCurrObj.Angle != null && osuLastObj0.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj0.Angle.Value;

                // SNAP

                // We reward wide angles on snap.
                snapAngleBuff = linearDifficulty * calculateAngleSpline(currAngle, false) * Math.Min(minVelocity, (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime));

                // nerf repeated wide angles
                snapAngleBuff *= Math.Pow(calculateAngleSpline(lastAngle, true), 2);

                // FLOW

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

            // minimum velocity change is 20%
            double clampedVelocityChange = Math.Max(0, Math.Abs(currVelocity - prevVelocity) - minVelocity * 0.2) / 0.8;
            // 1.5 is a multiplier for velocity change in flow
            flowDifficulty += 1.5 * linearDifficulty * clampedVelocityChange * rhythmRatio;

            //Console.WriteLine($"Old - {baseFlow:0.00}, New - {flowDifficulty:0.00}, Ratio - {flowDifficulty / baseFlow}");

            double snapVelocityBuff = linearDifficulty * Math.Max(0, Math.Min(Math.Abs(currVelocity - prevVelocity) - Math.Min(currVelocity, prevVelocity), minVelocity)) * rhythmRatio;

            if (currVelocity > prevVelocity)
                snapDifficulty += Math.Max(snapAngleBuff, snapVelocityBuff) + Math.Min(snapAngleBuff, snapVelocityBuff) * minVelocity / maxVelocity;
            else
                snapDifficulty += snapAngleBuff + snapVelocityBuff;

            // Apply balancing parameters.
            flowDifficulty *= 1.37;
            snapDifficulty *= 0.97;

            //double decayDuringDeltaT = Math.Pow(strainDecayBase, current.DeltaTime / 1000);
            //double limitOfStrain = flowDifficulty / (1 - decayDuringDeltaT);

            //Console.WriteLine($"Object {current.BaseObject.StartTime}, strain {flowDifficulty:0.00}, limit {limitOfStrain:0.00}");

            return (snapDifficulty, flowDifficulty);
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
                difficulty.snap = difficulty.flow * Math.Pow(difficulty.flow / difficulty.snap, 1.0);

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double arBuff = getHighARBonus(osuCurrObj);
            // WARNING: VERY QUESTIONABLE LENGTH BONUS FOR HIGH AR
            arBuff *= 1 - Math.Pow(2.7, -(osuCurrObj.Index + 120) / 120);
            arBuff = Math.Max(1, arBuff);

            return arBuff * applyRemainingBonusesTo(current, withSliderTravelDistance, strainDecayBase, minStrain);
        }

        public static double EvaluateFlowStrainOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, (double snap, double flow) difficulty)
        {
            if (isInvalid(current))
                return 0;

            if (difficulty.snap < difficulty.flow)
                difficulty.flow = difficulty.snap * Math.Pow(difficulty.snap / difficulty.flow, 2.0);

            double minStrain = Math.Min(difficulty.snap, difficulty.flow);

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double arBuff = getHighARBonus(osuCurrObj);
            // WARNING: VERY QUESTIONABLE LENGTH BONUS FOR HIGH AR
            arBuff *= 1 - Math.Pow(2.7, -(osuCurrObj.Index + 240) / 240); // 2 times more cuz usually flow is streams
            arBuff = Math.Max(1, arBuff);

            return arBuff * applyRemainingBonusesTo(current, withSliderTravelDistance, strainDecayBase, minStrain);
        }

        private static double getHighARBonus(OsuDifficultyHitObject osuCurrObj)
        {
            const double max_bonus = 0.3;

            double arBuff = 1.0;

            // follow lines make high AR easier, so apply nerf if object isn't new combo
            double adjustedApproachTime = osuCurrObj.ApproachRateTime + Math.Max(0, (osuCurrObj.FollowLineTime - 200) / 25);

            // we are assuming that 150ms is a complete memory and the bonus will be maximal (1.4) on this
            if (adjustedApproachTime < 150)
                arBuff += max_bonus;

            // bonus for AR starts from AR10.3, balancing bonus based on high SR cuz we don't have density calculation
            else if (adjustedApproachTime < 400)
            {
                arBuff += max_bonus * (1 + Math.Cos(Math.PI * 0.4 * (adjustedApproachTime - 150) / 100)) / 2;
            }

            return arBuff;
        }

        private static double applyRemainingBonusesTo(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, double aimStrain)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);

            double linearDifficulty = 32.0 / osuCurrObj.Radius;
            double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;

            // Buff cases where the holding of a slider makes the subsequent jump harder, even with leniency abuse.
            aimStrain = Math.Max(aimStrain, (aimStrain - linearDifficulty * 2.4 * osuCurrObj.Radius / Math.Min(osuCurrObj.MovementTime, osuLastObj0.MovementTime)) * (osuCurrObj.StrainTime / osuCurrObj.MovementTime));

            // Apply small CS buff.
            double smallCSBonus = 1 + Math.Pow(23.04 / osuCurrObj.Radius, 4.5) / 25; // cs7 have 1.04x multiplier
            aimStrain *= smallCSBonus;

            // Arbitrary cap to bonuses because balancing is hard.
            aimStrain = Math.Min(aimStrain, linearDifficulty * currVelocity * 3.25);

            // Slider stuff.
            double sustainedSliderStrain = 0.0;

            if (osuCurrObj.SliderSubObjects.Count != 0 && withSliderTravelDistance)
                sustainedSliderStrain = calculateSustainedSliderStrain(osuCurrObj, strainDecayBase, withSliderTravelDistance);

            // Apply slider strain with constant adjustment
            aimStrain += 1.5 * sustainedSliderStrain;

            return aimStrain;
        }

        // Big spacing buff
        private static double getAngleMultiplier(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);

            double linearDifficulty = 32.0 / osuCurrObj.Radius;

            double snapDifficulty = linearDifficulty * (osuCurrObj.Movement.Length / (osuCurrObj.StrainTime - 20) + (osuCurrObj.Radius * 2) / (Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime) - 20));
            double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;
            double prevVelocity = osuLastObj0.Movement.Length / osuLastObj0.StrainTime;

            double adjustedSnapDifficulty = snapDifficulty;

            if (osuCurrObj.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;

                // We reward wide angles on snap.
                adjustedSnapDifficulty += linearDifficulty * calculateAngleSpline(currAngle, false) * Math.Min(Math.Min(currVelocity, prevVelocity), (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime));
            }

            return adjustedSnapDifficulty / snapDifficulty;
        }
        public static double AdjustStrainDecay(DifficultyHitObject current, double strainDecayBase)
        {
            if (isInvalid(current))
                return strainDecayBase;

            var osuCurrObj = (OsuDifficultyHitObject)current;

            // distance in circles
            double normalisedDistance = 0.5 * osuCurrObj.Movement.Length / osuCurrObj.Radius;
            normalisedDistance *= getAngleMultiplier(current);

            // random values that work, you can check desmos
            // double targetDistance = normalisedDistance - Math.Log(Math.Pow(2, normalisedDistance) + 2.45, 6) + 0.92; // old variant

            const double basic_k = 0.7, target_bpm_point = 120; //120 = 250bpm
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

        private static double calculateSustainedSliderStrain(OsuDifficultyHitObject osuCurrObj, double strainDecayBase, bool withSliderTravelDistance)
        {
            int index = 0;

            double sliderRadius = 2.4 * osuCurrObj.Radius;
            double linearDifficulty = 32.0 / osuCurrObj.Radius;

            var historyVector = new Vector2(0,0);
            double historyTime = 0;
            double historyDistance = 0;

            double peakStrain = 0;
            double currentStrain = 0;

            foreach (var subObject in osuCurrObj.SliderSubObjects)
            {
                if (index == osuCurrObj.SliderSubObjects.Count && !withSliderTravelDistance)
                    break;

                double noteStrain = 0;

                if (index == 0 && osuCurrObj.SliderSubObjects.Count > 1)
                    noteStrain = Math.Max(0, linearDifficulty * subObject.Movement.Length) / subObject.StrainTime;

                historyVector += subObject.Movement;
                historyTime += subObject.StrainTime;
                historyDistance += subObject.Movement.Length;

                if (historyVector.Length > sliderRadius * 2.0)
                {
                    noteStrain += linearDifficulty * historyDistance / historyTime;

                    historyVector = new Vector2(0,0);
                    historyTime = 0;
                    historyDistance = 0;
                }

                currentStrain *= Math.Pow(strainDecayBase, subObject.StrainTime / 1000.0); // TODO bug here using strainTime.
                currentStrain += noteStrain;

                index += 1;
            }

            if (historyTime > 0 && withSliderTravelDistance)
            {
                if (osuCurrObj.SliderSubObjects.Count > 1)
                    currentStrain += Math.Max(0, linearDifficulty * Math.Max(0, historyVector.Length) / historyTime);
                else
                    currentStrain += Math.Max(0, linearDifficulty * Math.Max(0, historyVector.Length - 2 * osuCurrObj.Radius) / historyTime);
            }

            return Math.Max(currentStrain, peakStrain);
        }

        private static double calculateAngleSpline(double angle, bool reversed)
        {
            angle = Math.Abs(angle);
            if (reversed)
                return 1 - Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 3.0, 5 * Math.PI / 6.0) - Math.PI / 3), 2.0);

            return Math.Pow(Math.Sin((Math.Clamp(angle, Math.PI / 3.0, 3 * Math.PI / 4.0) - Math.PI / 3) * 1.2), 0.5);

        }
    }
}
