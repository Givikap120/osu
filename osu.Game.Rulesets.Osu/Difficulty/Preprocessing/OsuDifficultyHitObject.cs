// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultyHitObject : DifficultyHitObject
    {
        private const int min_delta_time = 30;

        protected new OsuHitObject BaseObject => (OsuHitObject)base.BaseObject;

        /// <summary>
        /// Milliseconds elapsed since the start time of the previous <see cref="OsuDifficultyHitObject"/>, with a minimum of 25ms.
        /// </summary>
        public readonly double StrainTime;

        /// <summary>
        /// Milliseconds elapsed since the lazy end time of the previous <see cref="OsuDifficultyHitObject"/>, with a minimum of 25ms.
        /// </summary>
        public readonly double MovementTime;

        /// <summary>
        /// Raw osu!pixel vector from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public Vector2 Movement { get; private set; }

        /// <summary>
        /// Raw osu!pixel vector from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public Vector2 SliderlessMovement { get; private set; }

        /// <summary>
        /// The List of raw osu!pixel vectors from the start position of the previous <see cref="OsuDifficultyHitObject"/> to the end position of the previous <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public List<OsuDifficultySliderSubObject> SliderSubObjects { get; private set; } = new();

        /// <summary>
        /// Angle the player has to take to hit this <see cref="OsuDifficultyHitObject"/>.
        /// Calculated as the angle between the circles (current-2, current-1, current).
        /// </summary>
        public double? Angle { get; private set; }

        /// <summary>
        /// Retrieves the full hit window for a Great <see cref="HitResult"/>.
        /// </summary>
        public double HitWindowGreat { get; private set; }

        /// <summary>
        /// Retrieves the preempt time for the object.
        /// </summary>
        public double ApproachRateTime { get; private set; }

        /// <summary>
        /// Retrieves the radius of the this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double Radius { get; private set; }

        private readonly OsuHitObject? lastLastObject;
        private readonly OsuHitObject lastObject;

        public OsuDifficultyHitObject(HitObject hitObject, HitObject lastObject, HitObject? lastLastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            this.lastLastObject = lastLastObject as OsuHitObject;
            this.lastObject = (OsuHitObject)lastObject;

            // Capped to 25ms to prevent difficulty calculation breaking from simultaneous objects.
            StrainTime = Math.Max(DeltaTime, min_delta_time);

            if (BaseObject is Slider sliderObject)
                HitWindowGreat = 2 * sliderObject.HeadCircle.HitWindows.WindowFor(HitResult.Great) / clockRate;
            else
                HitWindowGreat = 2 * BaseObject.HitWindows.WindowFor(HitResult.Great) / clockRate;

            Radius = BaseObject.Radius;

            ApproachRateTime = BaseObject.TimePreempt / clockRate;

            MovementTime = StrainTime;

            if (lastObject is Slider lastSlider)
            {
                setSliderSubObjects(lastSlider, clockRate);
                MovementTime = Math.Max(MovementTime - lastSlider.LazyTravelTime, min_delta_time);
            }

            Movement = BaseObject.StackedPosition - getEndCursorPosition(this.lastObject, clockRate);

            SliderlessMovement = BaseObject.StackedPosition - this.lastObject.StackedPosition;

            if (lastLastObject != null && !(lastLastObject is Spinner))// && !(lastObject is Slider))
            {
                Vector2 lastLastCursorPosition = getEndCursorPosition((OsuHitObject)lastLastObject, clockRate);
                Vector2 lastCursorPosition = getEndCursorPosition(this.lastObject, clockRate);

                Vector2 v1 = lastLastCursorPosition - this.lastObject.StackedPosition;
                Vector2 v2 = this.BaseObject.StackedPosition - lastCursorPosition;

                float dot = Vector2.Dot(v1, v2);
                float det = v1.X * v2.Y - v1.Y * v2.X;

                Angle = Math.Atan2(det, dot);
            }
        }

        private IList<HitObject> computeSliderCursorEnd(Slider slider, double clockRate)
        {
            // TODO: This commented version is actually correct by the new lazer implementation, but intentionally held back from
            // difficulty calculator to preserve known behaviour.
            // double trackingEndTime = Math.Max(
            //     // SliderTailCircle always occurs at the final end time of the slider, but the player only needs to hold until within a lenience before it.
            //     slider.Duration + SliderEventGenerator.TAIL_LENIENCY,
            //     // There's an edge case where one or more ticks/repeats fall within that leniency range.
            //     // In such a case, the player needs to track until the final tick or repeat.
            //     slider.NestedHitObjects.LastOrDefault(n => n is not SliderTailCircle)?.StartTime ?? double.MinValue
            // );

            double trackingEndTime = Math.Max(
                slider.StartTime + slider.Duration + SliderEventGenerator.TAIL_LENIENCY,
                slider.StartTime + slider.Duration / 2
            );

            IList<HitObject> nestedObjects = slider.NestedHitObjects;

            SliderTick? lastRealTick = slider.NestedHitObjects.OfType<SliderTick>().LastOrDefault();

            if (lastRealTick?.StartTime > trackingEndTime)
            {
                trackingEndTime = lastRealTick.StartTime;

                // When the last tick falls after the tracking end time, we need to re-sort the nested objects
                // based on time. This creates a somewhat weird ordering which is counter to how a user would
                // understand the slider, but allows a zero-diff with known diffcalc output.
                //
                // To reiterate, this is definitely not correct from a difficulty calculation perspective
                // and should be revisited at a later date (likely by replacing this whole code with the commented
                // version above).
                List<HitObject> reordered = nestedObjects.ToList();

                reordered.Remove(lastRealTick);
                reordered.Add(lastRealTick);

                nestedObjects = reordered;
            }

            slider.LazyTravelTime = (trackingEndTime - slider.StartTime) / clockRate;

            double endTimeMin = slider.LazyTravelTime / (slider.SpanDuration / clockRate);
            if (endTimeMin % 2 >= 1)
                endTimeMin = 1 - endTimeMin % 1;
            else
                endTimeMin %= 1;

            slider.LazyEndPosition = slider.StackedPosition + slider.Path.PositionAt(endTimeMin);

            return nestedObjects;
        }

        private void setSliderSubObjects(Slider slider, double clockRate)
        {
            IList<HitObject> nestedObjects = computeSliderCursorEnd(slider, clockRate);
            Vector2 currCursorPosition = slider.StackedPosition;

            double trackingEndTime = Math.Max(
                slider.StartTime + slider.Duration + SliderEventGenerator.TAIL_LENIENCY,
                slider.StartTime + slider.Duration / 2
            );

            var endPosition = slider.LazyEndPosition ?? slider.EndPosition;

            for (int i = 1; i < nestedObjects.Count; i++)
            {
                var currMovementObj = (OsuHitObject)nestedObjects[i];
                
                if (i == nestedObjects.Count - 1)
                {
                    // Last object is the slider end, use lazy end instead of true pos.
                    Vector2 currMovement = Vector2.Subtract(endPosition, currCursorPosition);
                    double deltaTime = (trackingEndTime - nestedObjects[i - 1].StartTime) / clockRate;
                    if (deltaTime >= 5)
                        SliderSubObjects.Add(new OsuDifficultySliderSubObject(currMovement, deltaTime));
                }
                else
                {
                    Vector2 currMovement = Vector2.Subtract(currMovementObj.StackedPosition, currCursorPosition);
                    double deltaTime = (currMovementObj.StartTime - nestedObjects[i - 1].StartTime) / clockRate;
                    if (deltaTime >= 5)
                    {
                        SliderSubObjects.Add(new OsuDifficultySliderSubObject(currMovement, deltaTime));
                        currCursorPosition = currMovementObj.StackedPosition;
                    }
                }
            }
        }

        private Vector2 getEndCursorPosition(OsuHitObject hitObject, double clockRate)
        {
            Vector2 pos = hitObject.StackedPosition;

            if (hitObject is Slider slider)
            {
                computeSliderCursorEnd(slider, clockRate);
                pos = slider.LazyEndPosition ?? pos;
            }

            return pos;
        }

        public double OpacityAt(double time, bool hidden)
        {
            if (time > BaseObject.StartTime)
            {
                // Consider a hitobject as being invisible when its start time is passed.
                // In reality the hitobject will be visible beyond its start time up until its hittable window has passed,
                // but this is an approximation and such a case is unlikely to be hit where this function is used.
                return 0.0;
            }

            double fadeInStartTime = BaseObject.StartTime - BaseObject.TimePreempt;
            double fadeInDuration = BaseObject.TimeFadeIn;

            if (hidden)
            {
                // Taken from OsuModHidden.
                double fadeOutStartTime = BaseObject.StartTime - BaseObject.TimePreempt + BaseObject.TimeFadeIn;
                double fadeOutDuration = BaseObject.TimePreempt * OsuModHidden.FADE_OUT_DURATION_MULTIPLIER;

                return Math.Min
                (
                    Math.Clamp((time - fadeInStartTime) / fadeInDuration, 0.0, 1.0),
                    1.0 - Math.Clamp((time - fadeOutStartTime) / fadeOutDuration, 0.0, 1.0)
                );
            }

            return Math.Clamp((time - fadeInStartTime) / fadeInDuration, 0.0, 1.0);
        }
    }
}
