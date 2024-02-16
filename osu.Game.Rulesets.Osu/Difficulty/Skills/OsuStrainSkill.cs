// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using System.Linq;
using osu.Framework.Utils;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public abstract class OsuStrainSkill : StrainSkill
    {
        /// <summary>
        /// The default multiplier applied by <see cref="OsuStrainSkill"/> to the final difficulty value after all other calculations.
        /// May be overridden via <see cref="DifficultyMultiplier"/>.
        /// </summary>
        public const double DEFAULT_DIFFICULTY_MULTIPLIER = 1.06;

        /// <summary>
        /// The number of sections with the highest strains, which the peak strain reductions will apply to.
        /// This is done in order to decrease their impact on the overall difficulty of the map for this skill.
        /// </summary>
        protected virtual int ReducedSectionCount => 10;

        /// <summary>
        /// The baseline multiplier applied to the section with the biggest strain.
        /// </summary>
        protected virtual double ReducedStrainBaseline => 0.75;

        /// <summary>
        /// The final multiplier to be applied to <see cref="DifficultyValue"/> after all other calculations.
        /// </summary>
        protected virtual double DifficultyMultiplier => DEFAULT_DIFFICULTY_MULTIPLIER;

        protected OsuStrainSkill(Mod[] mods)
            : base(mods)
        {
        }

        protected double LogarithmicSummation(IEnumerable<double> strains, bool nerfDiffspikes)
        {
            List<double> strainsList = strains.Where(x => x > 0).OrderByDescending(x => x).ToList();

            if (nerfDiffspikes)
            {
                // We are reducing the highest strains first to account for extreme difficulty spikes
                for (int i = 0; i < Math.Min(strainsList.Count, ReducedSectionCount); i++)
                {
                    double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((float)i / ReducedSectionCount, 0, 1)));
                    strainsList[i] *= Interpolation.Lerp(ReducedStrainBaseline, 1.0, scale);
                }

                strains = strains.OrderByDescending(x => x).ToList();
            }

            int index = 0;
            //double weight = 1;
            double difficulty = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            foreach (double strain in strainsList)
            {
                double weight = (1.0 + (20.0 / (1 + index))) / (Math.Pow(index, 0.9) + 1.0 + (20.0 / (1.0 + index)));

                difficulty += strain * weight;

                //weight *= DecayWeight;
                index += 1;
            }

            return difficulty;
        }

        protected double GeometricSummation(IEnumerable<double> strains, bool nerfDiffspikes)
        {
            List<double> strainsList = strains.Where(x => x > 0).OrderByDescending(x => x).ToList();

            if (nerfDiffspikes)
            {
                // We are reducing the highest strains first to account for extreme difficulty spikes
                for (int i = 0; i < Math.Min(strainsList.Count, ReducedSectionCount); i++)
                {
                    double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((float)i / ReducedSectionCount, 0, 1)));
                    strainsList[i] *= Interpolation.Lerp(ReducedStrainBaseline, 1.0, scale);
                }

                strains = strains.OrderByDescending(x => x).ToList();
            }

            //int index = 0;
            double weight = 1;
            double difficulty = 0;

            // Difficulty is the weighted sum of the highest strains from every section.
            // We're sorting from highest to lowest strain.
            foreach (double strain in strainsList)
            {
                difficulty += strain * weight;
                weight *= DecayWeight;
            }

            return difficulty;
        }

        public override double DifficultyValue()
        {
            double difficulty = LogarithmicSummation(GetCurrentStrainPeaks(), false);
            return difficulty * DifficultyMultiplier;
        }

        public override double AbstractDifficultyValue()
        {
            double difficulty = GeometricSummation(GetCurrentStrainPeaks(), false);
            return difficulty * DifficultyMultiplier;
        }
    }
}
